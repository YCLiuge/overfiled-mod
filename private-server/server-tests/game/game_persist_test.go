package game

import (
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/bytedance/sonic"
	"github.com/glebarez/sqlite"
	"github.com/gookit/slog"
	"gorm.io/gorm"
	"gorm.io/gorm/logger"
	"gucooing/lolo/db"
	"gucooing/lolo/game/model"
	"gucooing/lolo/pkg/log"
)

func TestLocalGameCloseFlushesAndReportsFailure(t *testing.T) {
	for _, tc := range []struct {
		name                 string
		panicTask, failWrite bool
	}{
		{name: "recent changes"}, {name: "panic in task", panicTask: true}, {name: "sql failure", failWrite: true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			path := filepath.Join(t.TempDir(), "shutdown.sqlite")
			if err := db.NewDB(&db.Option{Type: db.Sqlite, Dsn: path, MaxIdleConns: 1, MaxOpenConns: 1, PersistShardNum: 1, PersistBufSize: 2}); err != nil {
				t.Fatal(err)
			}
			t.Cleanup(func() {
				if err := db.Close(); err != nil {
					t.Error(err)
				}
			})
			if tc.failWrite {
				inspect, err := gorm.Open(sqlite.Open(path), &gorm.Config{Logger: logger.Default.LogMode(logger.Silent)})
				if err != nil {
					t.Fatal(err)
				}
				t.Cleanup(func() { c, _ := inspect.DB(); _ = c.Close() })
				if err = inspect.Exec(`CREATE TRIGGER fail_shutdown BEFORE INSERT ON of_game BEGIN SELECT RAISE(FAIL, 'injected shutdown failure'); END`).Error; err != nil {
					t.Fatal(err)
				}
			}
			oldLog, oldTicks := log.Game, model.OneSTickCount
			log.Game = slog.NewStdLogger()
			model.OneSTickCount = 20
			defer func() { log.Game = oldLog; model.OneSTickCount = oldTicks }()
			p := &model.Player{UserId: 81, LastSaveTime: time.Now(), ActiveTime: time.Now()}
			g := &Game{userMap: map[uint32]*model.Player{81: p}, taskChan: make(chan TaskInterface, 1), doneChan: make(chan struct{}), stoppedChan: make(chan struct{}), worldTask: model.NewWorldTask()}
			changed := make(chan struct{})
			g.taskChan <- &FuncTask{UserId: 81, Fn: func(p *model.Player) {
				p.InstanceIndex = 23
				close(changed)
				if tc.panicTask {
					panic("injected task panic")
				}
			}}
			go g.gameMainLoop()
			select {
			case <-changed:
			case <-time.After(5 * time.Second):
				t.Fatal("game task never ran")
			}
			errs := make(chan error, 2)
			var wg sync.WaitGroup
			for i := 0; i < 2; i++ {
				wg.Add(1)
				go func() { defer wg.Done(); errs <- g.Close() }()
			}
			done := make(chan struct{})
			go func() { wg.Wait(); close(done) }()
			select {
			case <-done:
			case <-time.After(5 * time.Second):
				t.Fatal("Close deadlocked")
			}
			close(errs)
			for err := range errs {
				if tc.panicTask || tc.failWrite {
					if err == nil {
						t.Fatal("shutdown failure was lost")
					}
				} else if err != nil {
					t.Fatal(err)
				}
				if tc.panicTask && !strings.Contains(err.Error(), "injected task panic") {
					t.Fatalf("wrong panic error: %v", err)
				}
			}
			if !tc.failWrite {
				row, err := db.GetOFGameByUserId(81)
				if err != nil {
					t.Fatal(err)
				}
				var loaded model.Player
				if err = sonic.Unmarshal(row.BinData, &loaded); err != nil {
					t.Fatal(err)
				}
				if loaded.InstanceIndex != 23 {
					t.Fatalf("Close failed to flush recent change: %d", loaded.InstanceIndex)
				}
			}
		})
	}
}
