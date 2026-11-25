package game

import (
	"bytes"
	"encoding/json"
	"testing"

	"github.com/gookit/slog"
	pb "google.golang.org/protobuf/proto"
	"gucooing/lolo/config"
	"gucooing/lolo/game/model"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/cmd"
	"gucooing/lolo/protocol/proto"
)

func TestAbilityBadgeHandlersReplyAndRoutes(t *testing.T) {
	oldConf, oldApp, oldGame := config.CONF, log.App, log.Game
	defer func() { config.CONF = oldConf; log.App = oldApp; log.Game = oldGame }()
	config.CONF = &config.Config{Game: &config.Game{Sandbox: true}, Resources: &config.Resources{ResourcePath: "../Resource", DataPath: "../data"}}
	log.App = slog.NewStdLogger()
	log.Game = log.App
	gdconf.LoadGameConfig()
	g := new(Game)
	g.newRouter()
	conn := new(captureConn)
	p := &model.Player{Conn: conn}
	p.GetItemModel().AddItemBase(301, 10)
	call := func(id uint32, body pb.Message) pb.Message {
		t.Helper()
		handler := g.handlerFuncRouteMap[id]
		if handler == nil {
			t.Fatalf("missing badge route %d", id)
		}
		conn.bodies = nil
		conn.packets = nil
		handler(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 731}, Body: body})
		if len(conn.bodies) != 1 || len(conn.packets) != 1 || conn.packets[0] != 731 {
			t.Fatal("request did not receive exactly one correlated reply")
		}
		out := conn.bodies[0]
		wire, err := pb.Marshal(out)
		if err != nil {
			t.Fatal(err)
		}
		round := out.ProtoReflect().Type().New().Interface()
		if err := pb.Unmarshal(wire, round); err != nil || !pb.Equal(round, out) {
			t.Fatal("response protobuf roundtrip", err)
		}
		return round
	}
	list := call(cmd.AbilityBadgeListReq, &proto.AbilityBadgeListReq{SceneId: 1}).(*proto.AbilityBadgeListRsp)
	if list.Status != proto.StatusCode_StatusCode_OK || len(list.AbilityBadgePages) != 1 || list.AbilityBadgePages[0].Page != 1 || len(list.AbilityBadgePages[0].ActiveBoxIds) != 29 {
		t.Fatal("scene list malformed")
	}
	// A complete page responds idempotently without touching inventory or archive.
	before, _ := json.Marshal(p)
	for i := 0; i < 2; i++ {
		rsp := call(cmd.AbilityBadgePageBoxActiveReq, &proto.AbilityBadgePageBoxActiveReq{Page: 1, BoxId: 1}).(*proto.AbilityBadgePageBoxActiveRsp)
		if rsp.Status != proto.StatusCode_StatusCode_OK || rsp.Page != 1 || rsp.BoxId != 1 || len(rsp.Items) != 0 {
			t.Fatal("repeated max activation failed")
		}
	}
	after, _ := json.Marshal(p)
	if !bytes.Equal(before, after) {
		t.Fatal("repeat consumed inventory or modified save")
	}
	invalid := call(cmd.AbilityBadgePageBoxActiveReq, &proto.AbilityBadgePageBoxActiveReq{Page: 1, BoxId: 3}).(*proto.AbilityBadgePageBoxActiveRsp)
	if invalid.Status != proto.StatusCode_StatusCode_BAD_REQ || invalid.BoxId != 3 {
		t.Fatal("placeholder not rejected")
	}
	if call(cmd.AbilityBadgeListReq, &proto.AbilityBadgeListReq{SceneId: 999}).(*proto.AbilityBadgeListRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("invalid scene accepted")
	}
	if call(cmd.AbilityBadgePageRewardReq, &proto.AbilityBadgePageRewardReq{Page: 1}).(*proto.AbilityBadgePageRewardRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("unverified page reward granted")
	}
	if call(cmd.AbilityBadgeAchieveRewardReq, &proto.AbilityBadgeAchieveRewardReq{AchieveIds: []uint32{50101010}}).(*proto.AbilityBadgeAchieveRewardRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("unverified achievement reward granted")
	}
	after, _ = json.Marshal(p)
	if !bytes.Equal(before, after) {
		t.Fatal("invalid requests mutated save")
	}
	config.CONF.Game.Sandbox = false
	if call(cmd.AbilityBadgePageBoxActiveReq, &proto.AbilityBadgePageBoxActiveReq{Page: 1, BoxId: 1}).(*proto.AbilityBadgePageBoxActiveRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("ordinary activation silently accepted")
	}
}
