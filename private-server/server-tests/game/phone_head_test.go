package game

import (
	"path/filepath"
	"reflect"
	"testing"

	"github.com/glebarez/sqlite"
	"github.com/gookit/slog"
	pb "google.golang.org/protobuf/proto"
	"gorm.io/gorm"
	"gorm.io/gorm/schema"
	"gucooing/lolo/config"
	"gucooing/lolo/db"
	"gucooing/lolo/game/model"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/cmd"
	"gucooing/lolo/protocol/proto"
)

func TestLocalPhoneHeadHandlers(t *testing.T) {
	oldConf, oldApp, oldGame := config.CONF, log.App, log.Game
	defer func() { config.CONF = oldConf; log.App = oldApp; log.Game = oldGame }()
	config.CONF = &config.Config{Resources: &config.Resources{ResourcePath: "../Resource", DataPath: "../data"}}
	log.App = slog.NewStdLogger()
	log.Game = log.App
	gdconf.LoadGameConfig()
	dsn := filepath.Join(t.TempDir(), "phone-head.sqlite")
	if err := db.NewDB(&db.Option{Type: db.Sqlite, Dsn: dsn, MaxIdleConns: 1, MaxOpenConns: 1, PersistShardNum: 1, PersistBufSize: 2}); err != nil {
		t.Fatal(err)
	}
	defer db.Close()
	// Independent connection verifies durable rows rather than the basic cache.
	store, err := gorm.Open(sqlite.Open(dsn), &gorm.Config{NamingStrategy: schema.NamingStrategy{SingularTable: true}})
	if err != nil {
		t.Fatal(err)
	}
	sqlDB, err := store.DB()
	if err != nil {
		t.Fatal(err)
	}
	defer sqlDB.Close()
	conn := new(captureConn)
	p := &model.Player{UserId: 724201, Conn: conn}
	p.ApplySandbox()
	p.ApplySandboxAppearanceExtras()
	basic, err := db.GetGameBasicOrCreate(p.UserId)
	if err != nil {
		t.Fatal(err)
	}
	if basic.Level != 30 {
		t.Fatal("new account must default to level 30")
	}
	if basic.PhoneCase != 0 {
		t.Fatal("new phone case must retain protocol default zero")
	}
	g := new(Game)
	g.newRouter()
	ch := &ChannelInfo{serverSceneSyncChan: make(chan *ServerSceneSyncCtx, 4)}
	sp := &ScenePlayer{Player: p, channelInfo: ch}
	sp.CurScene = &model.ScenePlayerInfo{Player: p, Team: p.GetTeamModel().GetTeamInfo()}
	g.getWordInfo().allScenePlayer.Store(p.UserId, sp)
	readStored := func() db.OFGameBasic {
		t.Helper()
		var got db.OFGameBasic
		if err := store.First(&got, "user_id = ?", p.UserId).Error; err != nil {
			t.Fatal(err)
		}
		return got
	}
	assertSync := func(want bool) {
		t.Helper()
		select {
		case ctx := <-ch.serverSceneSyncChan:
			if !want || ctx.ScenePlayer != sp || ctx.ActionType != proto.SceneActionType_SceneActionType_UPDATE_APPEARANCE || ctx.CharacterId != 0 {
				t.Fatalf("unexpected scene action: %+v", ctx)
			}
			ch.sceneServerDatas = make(map[uint32]*proto.ServerSceneSyncData)
			ch.serverSceneSync(ctx)
			payload := ch.sceneServerDatas[p.UserId].ServerData[0].Player
			wire, err := pb.Marshal(payload)
			if err != nil {
				t.Fatal(err)
			}
			round := new(proto.ScenePlayer)
			if err = pb.Unmarshal(wire, round); err != nil || round.PhoneCase != basic.PhoneCase || ch.GetPbScenePlayer(sp).PhoneCase != basic.PhoneCase {
				t.Fatal("scene change/join lost actual phone case")
			}
		default:
			if want {
				t.Fatal("successful change missing appearance action")
			}
		}
	}
	for _, level := range []uint32{1, 29, 30, 40} {
		if err := db.UpGameBasic(p.UserId, func(b *db.OFGameBasic) bool { b.Level = level; b.Exp = 123; return true }); err != nil {
			t.Fatal(err)
		}
		before := readStored()
		if err := applySandboxAccountLevel(p.UserId); err != nil {
			t.Fatal(err)
		}
		got := readStored()
		want := level
		if want < 30 {
			want = 30
		}
		if got.Level != want || basic.Level != want || got.Exp != 123 || got.Head != before.Head || got.PhoneCase != before.PhoneCase {
			t.Fatal("sandbox level changed unrelated data or failed to persist")
		}
		if err := applySandboxAccountLevel(p.UserId); err != nil {
			t.Fatal(err)
		}
		if !reflect.DeepEqual(got, readStored()) {
			t.Fatal("repeat level floor changed durable data")
		}
	}
	cat := gdconf.GetAppearanceExtras()
	if cat == nil || len(cat.Categories["head"]) < 2 || len(cat.Categories["phoneCase"]) < 2 {
		t.Fatal("missing real head/phoneCase catalog")
	}
	tests := []struct {
		name     string
		command  uint32
		ids      []uint32
		request  func(uint32) pb.Message
		typedNil pb.Message
		result   func(pb.Message) (proto.StatusCode, uint32)
		value    func(db.OFGameBasic) uint32
	}{
		{"head", cmd.ChangeHeadReq, cat.Categories["head"], func(id uint32) pb.Message { return &proto.ChangeHeadReq{Head: id} }, (*proto.ChangeHeadReq)(nil), func(m pb.Message) (proto.StatusCode, uint32) { r := m.(*proto.ChangeHeadRsp); return r.Status, r.Head }, func(b db.OFGameBasic) uint32 { return b.Head }},
		{"phoneCase", cmd.ChangePhoneCaseReq, cat.Categories["phoneCase"], func(id uint32) pb.Message { return &proto.ChangePhoneCaseReq{PhoneCase: id} }, (*proto.ChangePhoneCaseReq)(nil), func(m pb.Message) (proto.StatusCode, uint32) {
			r := m.(*proto.ChangePhoneCaseRsp)
			return r.Status, r.PhoneCase
		}, func(b db.OFGameBasic) uint32 { return b.PhoneCase }},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			handler := g.handlerFuncRouteMap[tt.command]
			if handler == nil {
				t.Fatal("request not registered")
			}
			send := func(body pb.Message) (proto.StatusCode, uint32) {
				t.Helper()
				conn.bodies, conn.packets = nil, nil
				handler(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 924}, Body: body})
				if len(conn.bodies) != 1 || len(conn.packets) != 1 || conn.packets[0] != 924 {
					t.Fatal("wrong response count or PacketId")
				}
				return tt.result(conn.bodies[0])
			}
			id := tt.ids[0]
			if !p.OwnsAppearanceExtra(tt.name, id) {
				t.Fatal("sandbox did not grant configured item")
			}
			status, value := send(tt.request(id))
			if status != proto.StatusCode_StatusCode_OK || value != id {
				t.Fatalf("change failed: %v %v", status, value)
			}
			assertSync(true)
			stored := readStored()
			if tt.value(stored) != id || tt.value(*basic) != id {
				t.Fatal("successful change missing from SQLite/cache")
			}
			status, value = send(tt.request(id))
			if status != proto.StatusCode_StatusCode_OK || value != id {
				t.Fatal("repeat change failed")
			}
			assertSync(true)
			if !reflect.DeepEqual(stored, readStored()) {
				t.Fatal("idempotent request changed durable data")
			}
			before := *basic
			invalid := []pb.Message{nil, tt.typedNil, &proto.PlayerMainDataReq{}, tt.request(0), tt.request(4294967295)}
			// An owned item from another category is not a valid choice.
			other := cat.Categories["badge"]
			if len(other) == 0 {
				t.Fatal("missing cross-category fixture")
			}
			invalid = append(invalid, tt.request(other[0]))
			for _, body := range invalid {
				status, _ = send(body)
				if status != proto.StatusCode_StatusCode_BAD_REQ {
					t.Fatalf("invalid request accepted: %v", body)
				}
				assertSync(false)
				if !reflect.DeepEqual(before, *basic) || !reflect.DeepEqual(stored, readStored()) {
					t.Fatal("invalid request mutated state")
				}
			}
			// A valid catalog entry without its real inventory record must fail.
			owned := p.Item
			p.Item = &model.ItemModel{}
			status, _ = send(tt.request(id))
			p.Item = owned
			if status != proto.StatusCode_StatusCode_BAD_REQ {
				t.Fatal("unowned choice accepted")
			}
			assertSync(false)
			if !reflect.DeepEqual(before, *basic) || !reflect.DeepEqual(stored, readStored()) {
				t.Fatal("unowned request mutated state")
			}
			// Force the actual SQL update to fail after UpGameBasic has mutated its
			// cached object. Both that object and the disk row must be unchanged.
			table := store.NamingStrategy.TableName("OFGameBasic")
			trigger := "CREATE TRIGGER reject_phone_head BEFORE UPDATE ON " + table + " BEGIN SELECT RAISE(ABORT, 'test write failure'); END"
			if err := store.Exec(trigger).Error; err != nil {
				t.Fatal(err)
			}
			alternative := tt.ids[1]
			if alternative == id {
				t.Fatal("duplicate catalog fixture")
			}
			status, _ = send(tt.request(alternative))
			if err := store.Exec("DROP TRIGGER reject_phone_head").Error; err != nil {
				t.Fatal(err)
			}
			if status != proto.StatusCode_StatusCode_PLAYER_NOT_FOUND {
				t.Fatal("storage failure reported success")
			}
			assertSync(false)
			if !reflect.DeepEqual(before, *basic) || !reflect.DeepEqual(stored, readStored()) {
				t.Fatal("storage failure did not restore cache/durable state")
			}
			// Missing database identity also fails without appearance notifications.
			userID := p.UserId
			p.UserId = 724299
			status, _ = send(tt.request(id))
			p.UserId = userID
			if status != proto.StatusCode_StatusCode_PLAYER_NOT_FOUND {
				t.Fatal("missing basic row accepted")
			}
			assertSync(false)
		})
	}
}
