package model

import (
	"path/filepath"
	"testing"
	"time"

	"github.com/bytedance/sonic"
	"github.com/glebarez/sqlite"
	"gorm.io/gorm"
	"gorm.io/gorm/logger"
	"gucooing/lolo/db"
)

func openPlayerTestDB(t *testing.T) *gorm.DB {
	t.Helper()
	path := filepath.Join(t.TempDir(), "players.sqlite")
	if err := db.NewDB(&db.Option{Type: db.Sqlite, Dsn: path, MaxIdleConns: 1, MaxOpenConns: 1, PersistShardNum: 1, PersistBufSize: 2}); err != nil {
		t.Fatal(err)
	}
	inspect, err := gorm.Open(sqlite.Open(path), &gorm.Config{Logger: logger.Default.LogMode(logger.Silent)})
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		if err := db.Close(); err != nil {
			t.Error(err)
		}
		conn, _ := inspect.DB()
		_ = conn.Close()
	})
	return inspect
}
func readPlayer(t *testing.T, id uint32) *Player {
	t.Helper()
	row, err := db.GetOFGameByUserId(id)
	if err != nil {
		t.Fatal(err)
	}
	var p Player
	if err = sonic.Unmarshal(row.BinData, &p); err != nil {
		t.Fatal(err)
	}
	return &p
}

func TestLocalFlushPlayerSavesRecentChangesAndLatestSnapshot(t *testing.T) {
	openPlayerTestDB(t)
	p := &Player{UserId: 71, LastSaveTime: time.Now(), ActiveTime: time.Now(), InstanceIndex: 1}
	if p.IsSave() {
		t.Fatal("test requires autosave throttle to be active")
	}
	if err := p.FlushPlayer(); err != nil {
		t.Fatal(err)
	}
	if got := readPlayer(t, 71).InstanceIndex; got != 1 {
		t.Fatalf("first roundtrip: %d", got)
	}
	// Hold the worker on an old snapshot while the owner makes a new change.
	entered, release := make(chan struct{}), make(chan struct{})
	db.Persist(71, func() error { close(entered); <-release; return nil })
	<-entered
	p.InstanceIndex = 2
	if err := p.SavePlayer(); err != nil {
		t.Fatal(err)
	}
	p.InstanceIndex = 3
	p.ActiveTime = time.Now()
	close(release)
	if err := p.FlushPlayer(); err != nil {
		t.Fatal(err)
	}
	if got := readPlayer(t, 71).InstanceIndex; got != 3 {
		t.Fatalf("flush lost change made after pending snapshot: %d", got)
	}
	if p.SaveLock || p.pendingSave != nil {
		t.Fatal("flush left save pending")
	}
}

func TestLocalReconnectPreservesSaveBoundary(t *testing.T) {
	last := time.Now().Add(-10 * time.Minute)
	p := &Player{LastSaveTime: last, ActiveTime: time.Now()}
	p.Init(nil, "reconnected")
	if !p.LastSaveTime.Equal(last) || !p.IsSave() {
		t.Fatal("reconnect postponed an overdue save")
	}
}

func TestLocalFlushPlayerFailureCanRetry(t *testing.T) {
	inspect := openPlayerTestDB(t)
	// The real SQL write fails, without replacing any production functions.
	if err := inspect.Exec(`CREATE TRIGGER fail_player_insert BEFORE INSERT ON of_game BEGIN SELECT RAISE(FAIL, 'injected save failure'); END`).Error; err != nil {
		t.Fatal(err)
	}
	last := time.Now().Add(-time.Minute)
	p := &Player{UserId: 72, InstanceIndex: 17, LastSaveTime: last, ActiveTime: time.Now()}
	if err := p.FlushPlayer(); err == nil {
		t.Fatal("expected real SQL failure")
	}
	if !p.LastSaveTime.Equal(last) || p.SaveLock || p.pendingSave != nil {
		t.Fatal("failed save changed success timestamp or retained lock")
	}
	if err := inspect.Exec(`DROP TRIGGER fail_player_insert`).Error; err != nil {
		t.Fatal(err)
	}
	if err := p.FlushPlayer(); err != nil {
		t.Fatalf("retry failed: %v", err)
	}
	if got := readPlayer(t, 72).InstanceIndex; got != 17 {
		t.Fatalf("retry roundtrip: %d", got)
	}
	if !p.LastSaveTime.After(last) {
		t.Fatal("successful retry did not update timestamp")
	}
}
