package game

import (
	"encoding/json"
	"github.com/gookit/slog"
	pb "google.golang.org/protobuf/proto"
	"gucooing/lolo/config"
	"gucooing/lolo/game/model"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/cmd"
	"gucooing/lolo/protocol/proto"
	"testing"
)

func TestLocalGachaRoutes(t *testing.T) {
	oldConf, oldApp, oldGame := config.CONF, log.App, log.Game
	defer func() { config.CONF, log.App, log.Game = oldConf, oldApp, oldGame }()
	config.CONF = &config.Config{Resources: &config.Resources{ResourcePath: "../Resource", DataPath: "../data"}}
	config.GetGame().Sandbox = true
	log.App = slog.NewStdLogger()
	log.Game = slog.NewStdLogger()
	gdconf.LoadGameConfig()
	c := new(captureConn)
	p := &model.Player{UserId: 42, Online: true, LoginUUID: "test", Conn: c}
	g := &Game{userMap: map[uint32]*model.Player{42: p}}
	g.newRouter()
	route := func(id uint32, body pb.Message) {
		c.bodies = nil
		c.packets = nil
		g.routeHandle(c, 42, "test", &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 71, MsgId: id}, Body: body})
	}
	route(cmd.GachaListReq, &proto.GachaListReq{})
	list := c.bodies[0].(*proto.GachaListRsp)
	if len(list.Gachas) != len(gdconf.GetSandboxGachas()) || c.packets[0] != 71 {
		t.Fatal("missing pools")
	}
	for _, pool := range gdconf.GetSandboxGachas() {
		id := uint32(pool.Conf.ID)
		if pool.IsOptional() {
			route(cmd.ChooseGachaReq, &proto.ChooseGachaReq{GachaId: id, ChooseGachaPool: uint32(pool.Conf.CardPoolRange[0])})
			if c.bodies[0].(*proto.ChooseGachaRsp).Status != proto.StatusCode_StatusCode_OK {
				t.Fatal("choose failed")
			}
		}
		route(cmd.GachaReq, &proto.GachaReq{GachaId: id})
		if len(c.bodies) != 2 {
			t.Fatalf("pool %d replies %v", id, c.bodies)
		}
		pack := c.bodies[0].(*proto.PackNotice)
		rsp := c.bodies[1].(*proto.GachaRsp)
		if c.packets[0] != 0 || c.packets[1] != 71 || rsp.Status != proto.StatusCode_StatusCode_OK || len(rsp.Items) != 10 || len(pack.Items) == 0 || !rsp.Info.IsFree || rsp.Info.GachaTimes != 0 {
			t.Fatalf("pool %d incomplete", id)
		}
		route(cmd.GachaRecordReq, &proto.GachaRecordReq{GachaId: id, Page: 1})
		records := c.bodies[0].(*proto.GachaRecordRsp)
		if len(records.Records) != 5 || records.TotalPage != 2 {
			t.Fatal("history")
		}
	}
	snapshot := func() string { b, _ := json.Marshal(p); return string(b) }
	before := snapshot()
	route(cmd.GachaReq, &proto.GachaReq{GachaId: 4294967295})
	if len(c.bodies) != 1 || c.bodies[0].(*proto.GachaRsp).Status == proto.StatusCode_StatusCode_OK || before != snapshot() {
		t.Fatal("invalid draw changed state")
	}
	route(cmd.ChooseGachaReq, &proto.ChooseGachaReq{GachaId: 9000, ChooseGachaPool: 2000})
	if c.bodies[0].(*proto.ChooseGachaRsp).Status == proto.StatusCode_StatusCode_OK || before != snapshot() {
		t.Fatal("invalid choose")
	}
	route(cmd.OptionalUpPoolItemReq, &proto.OptionalUpPoolItemReq{GachaId: 4000, ItemId: 999999})
	if c.bodies[0].(*proto.OptionalUpPoolItemRsp).Status == proto.StatusCode_StatusCode_OK || before != snapshot() {
		t.Fatal("invalid optional")
	}
	config.GetGame().Sandbox = false
	route(cmd.GachaReq, &proto.GachaReq{GachaId: 2000})
	if c.bodies[0].(*proto.GachaRsp).Status == proto.StatusCode_StatusCode_OK || before != snapshot() {
		t.Fatal("strict fake success")
	}
}
