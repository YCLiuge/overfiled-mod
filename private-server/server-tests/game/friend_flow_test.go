package game

import (
	"path/filepath"
	"testing"

	"github.com/glebarez/sqlite"
	"github.com/gookit/slog"
	pb "google.golang.org/protobuf/proto"
	"gorm.io/gorm"
	"gorm.io/gorm/schema"
	"gucooing/lolo/db"
	"gucooing/lolo/game/model"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/proto"
)

func friendFlowFixture(t *testing.T) (*Game, *model.Player, *model.Player, *gorm.DB) {
	t.Helper()
	path := filepath.Join(t.TempDir(), "friends.sqlite")
	if err := db.NewDB(&db.Option{Type: db.Sqlite, Dsn: path, MaxIdleConns: 1, MaxOpenConns: 1, PersistShardNum: 1, PersistBufSize: 2}); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		if err := db.Close(); err != nil {
			t.Error(err)
		}
	})
	inspect, err := gorm.Open(sqlite.Open(path), &gorm.Config{NamingStrategy: schema.NamingStrategy{SingularTable: true}})
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { c, _ := inspect.DB(); _ = c.Close() })
	old := log.Game
	log.Game = slog.NewStdLogger()
	t.Cleanup(func() { log.Game = old })
	a := &model.Player{UserId: 701, Conn: new(captureConn)}
	b := &model.Player{UserId: 702, Conn: new(captureConn)}
	for _, p := range []*model.Player{a, b} {
		if _, err := db.GetGameBasicOrCreate(p.UserId); err != nil {
			t.Fatal(err)
		}
	}
	return &Game{userMap: map[uint32]*model.Player{a.UserId: a, b.UserId: b}}, a, b, inspect
}
func friendMessage(body pb.Message) *alg.GameMsg {
	return &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 91}, Body: body}
}
func TestLocalFriendRequestAcceptDelete(t *testing.T) {
	g, a, b, _ := friendFlowFixture(t)
	g.FriendAdd(a, friendMessage(&proto.FriendAddReq{PlayerId: b.UserId}))
	ac, bc := a.Conn.(*captureConn), b.Conn.(*captureConn)
	if ac.bodies[len(ac.bodies)-1].(*proto.FriendAddRsp).Status != proto.StatusCode_StatusCode_OK {
		t.Fatal("apply failed")
	}
	if n := bc.bodies[0].(*proto.FriendHandleNotice); n.TargetPlayerId != a.UserId || n.Type != proto.FriendHandleType_FriendHandleType_APPLY {
		t.Fatal("missing incoming notice")
	}
	g.Friend(b, friendMessage(&proto.FriendReq{Type: proto.FriendListType_FriendListType_APPLY}))
	list := bc.bodies[len(bc.bodies)-1].(*proto.FriendRsp)
	if len(list.Info) != 1 || list.Info[0].Info.PlayerId != a.UserId {
		t.Fatalf("incoming list incorrect: %v", list)
	}
	g.Friend(a, friendMessage(&proto.FriendReq{Type: proto.FriendListType_FriendListType_APPLY}))
	if len(ac.bodies[len(ac.bodies)-1].(*proto.FriendRsp).Info) != 0 {
		t.Fatal("sender sees outgoing request as incoming")
	}
	g.FriendAdd(a, friendMessage(&proto.FriendAddReq{PlayerId: b.UserId}))
	if ac.bodies[len(ac.bodies)-1].(*proto.FriendAddRsp).Status != proto.StatusCode_StatusCode_FRIEND_APPLY_EXIST {
		t.Fatal("duplicate apply accepted")
	}
	g.FriendSearch(a, friendMessage(&proto.FriendSearchReq{SearchArgs: "702"}))
	if ac.bodies[len(ac.bodies)-1].(*proto.FriendSearchRsp).FriendStatus != proto.FriendStatus_FriendStatus_APPLY {
		t.Fatal("search lost pending status")
	}
	g.OtherPlayerInfo(b, friendMessage(&proto.OtherPlayerInfoReq{PlayerId: a.UserId}))
	if bc.bodies[len(bc.bodies)-1].(*proto.OtherPlayerInfoRsp).FriendStatus != proto.FriendStatus_FriendStatus_APPLY {
		t.Fatal("profile lost incoming pending status")
	}
	g.FriendHandle(b, friendMessage(&proto.FriendHandleReq{PlayerId: a.UserId, IsAgree: true}))
	if bc.bodies[len(bc.bodies)-1].(*proto.FriendHandleRsp).Status != proto.StatusCode_StatusCode_OK {
		t.Fatal("accept failed")
	}
	for _, pair := range [][2]uint32{{a.UserId, b.UserId}, {b.UserId, a.UserId}} {
		n, err := db.GetIsFiend(pair[0], pair[1])
		if err != nil || n != 1 {
			t.Fatal("missing reciprocal friendship", err)
		}
	}
	for _, c := range []*captureConn{ac, bc} {
		found := false
		for _, body := range c.bodies {
			if n, ok := body.(*proto.FriendHandleNotice); ok && n.Type == proto.FriendHandleType_FriendHandleType_ADD {
				found = true
			}
		}
		if !found {
			t.Fatal("missing add notice")
		}
	}
	before := len(ac.bodies)
	g.FriendHandle(b, friendMessage(&proto.FriendHandleReq{PlayerId: a.UserId, IsAgree: true}))
	if len(ac.bodies) != before || bc.bodies[len(bc.bodies)-1].(*proto.FriendHandleRsp).Status == proto.StatusCode_StatusCode_OK {
		t.Fatal("stale acceptance notified success")
	}
	g.FriendDel(a, friendMessage(&proto.FriendDelReq{PlayerId: b.UserId}))
	for _, id := range []uint32{a.UserId, b.UserId} {
		list, err := db.GetAllFiend(id)
		if err != nil || len(list) != 0 {
			t.Fatal("delete left reciprocal friendship", err)
		}
	}
}
func TestLocalFriendRejectMutualAndBlock(t *testing.T) {
	g, a, b, inspect := friendFlowFixture(t)
	apply := func() { g.FriendAdd(a, friendMessage(&proto.FriendAddReq{PlayerId: b.UserId})) }
	apply()
	g.FriendHandle(b, friendMessage(&proto.FriendHandleReq{PlayerId: a.UserId, IsAgree: false}))
	list, err := db.GetAllFriendApply(b.UserId)
	if err != nil || len(list) != 0 {
		t.Fatal("rejected request retained")
	}
	apply()
	g.FriendAdd(b, friendMessage(&proto.FriendAddReq{PlayerId: a.UserId}))
	n, err := db.GetIsFiend(a.UserId, b.UserId)
	if err != nil || n != 1 {
		t.Fatal("mutual apply did not accept")
	}
	if n, err := db.GetIsFriendApply(a.UserId, b.UserId); err != nil || n != 0 {
		t.Fatal("friend mistaken for pending application")
	}
	if err := db.DelFiend(a.UserId, b.UserId); err != nil {
		t.Fatal(err)
	}
	apply()
	if err := db.CreateFriendBlack(b.UserId, a.UserId, true); err != nil {
		t.Fatal(err)
	}
	if err := db.CreateFriendBlack(b.UserId, a.UserId, true); err != nil {
		t.Fatal("repeated block must be idempotent", err)
	}
	apply()
	ac := a.Conn.(*captureConn)
	if ac.bodies[len(ac.bodies)-1].(*proto.FriendAddRsp).Status != proto.StatusCode_StatusCode_FRIEND_BLACK {
		t.Fatal("reverse blacklist bypassed")
	}
	// An old pending row must not allow accepting a blocked player.
	if err := inspect.Create(&db.OFFriend{UserId: a.UserId, FriendId: b.UserId, Status: proto.FriendStatus_FriendStatus_APPLY}).Error; err != nil {
		t.Fatal(err)
	}
	if err := db.FriendHandleApply(a.UserId, b.UserId, true); err == nil {
		t.Fatal("accept bypassed blacklist")
	}
}
func TestLocalFriendFailedTransactionAndInvalidTargets(t *testing.T) {
	g, a, b, inspect := friendFlowFixture(t)
	ac, bc := a.Conn.(*captureConn), b.Conn.(*captureConn)
	for _, id := range []uint32{0, a.UserId, 4294967295} {
		g.FriendAdd(a, friendMessage(&proto.FriendAddReq{PlayerId: id}))
		if ac.bodies[len(ac.bodies)-1].(*proto.FriendAddRsp).Status == proto.StatusCode_StatusCode_OK {
			t.Fatal("invalid target accepted")
		}
	}
	g.FriendAdd(a, friendMessage(&proto.FriendAddReq{PlayerId: b.UserId}))
	if err := inspect.Exec("CREATE TRIGGER fail_reverse BEFORE INSERT ON of_friend WHEN NEW.user_id = 702 BEGIN SELECT RAISE(FAIL, 'injected reverse failure'); END").Error; err != nil {
		t.Fatal(err)
	}
	before := len(ac.bodies)
	g.FriendHandle(b, friendMessage(&proto.FriendHandleReq{PlayerId: a.UserId, IsAgree: true}))
	if len(ac.bodies) != before || bc.bodies[len(bc.bodies)-1].(*proto.FriendHandleRsp).Status == proto.StatusCode_StatusCode_OK {
		t.Fatal("failed transaction reported success")
	}
	n, err := db.GetIsFriendApply(a.UserId, b.UserId)
	if err != nil || n != 1 {
		t.Fatal("failed acceptance did not roll back")
	}
	if err := inspect.Exec("DROP TRIGGER fail_reverse").Error; err != nil {
		t.Fatal(err)
	}
	// A legacy mutual pending pair must promote both rows without uniqueness errors.
	if err := inspect.Create(&db.OFFriend{UserId: b.UserId, FriendId: a.UserId, Status: proto.FriendStatus_FriendStatus_APPLY, Alias: "keep"}).Error; err != nil {
		t.Fatal(err)
	}
	g.FriendHandle(b, friendMessage(&proto.FriendHandleReq{PlayerId: a.UserId, IsAgree: true}))
	row, err := db.GetFiend(b.UserId, a.UserId)
	if err != nil || row == nil || row.Alias != "keep" {
		t.Fatal("legacy pending reverse lost", err)
	}
}
