package game

import (
	"github.com/gookit/slog"
	"gucooing/lolo/config"
	"gucooing/lolo/game/model"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/cmd"
	"gucooing/lolo/protocol/proto"
	"testing"
)

func TestLocalTreasureIllustrationRouteAndRepeat(t *testing.T) {
	oldConf, oldApp, oldGame := config.CONF, log.App, log.Game
	defer func() { config.CONF = oldConf; log.App = oldApp; log.Game = oldGame }()
	config.CONF = &config.Config{Resources: &config.Resources{ResourcePath: "../Resource", DataPath: "../data"}}
	config.GetGame().Sandbox = true
	log.App = slog.NewStdLogger()
	log.Game = slog.NewStdLogger()
	gdconf.LoadGameConfig()
	c := new(captureConn)
	p := &model.Player{UserId: 42, Conn: c, Online: true, LoginUUID: "test"}
	g := &Game{userMap: map[uint32]*model.Player{42: p}}
	g.newRouter()
	for attempt := 0; attempt < 2; attempt++ {
		c.bodies = nil
		c.packets = nil
		g.routeHandle(c, 42, "test", &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 91, MsgId: cmd.TreasureIllustrationRewardReq}, Body: &proto.TreasureIllustrationRewardReq{ArchaeologyType: proto.EArchaeologyType(0), Id: 1}})
		replies, notices := 0, 0
		for i, body := range c.bodies {
			switch r := body.(type) {
			case *proto.TreasureIllustrationRewardRsp:
				replies++
				if r.Status != proto.StatusCode_StatusCode_OK || r.Id != 1 || r.ArchaeologyType != 0 || c.packets[i] != 91 {
					t.Fatalf("invalid response: %v", r)
				}
				if attempt == 0 && (len(r.Rewards) != 1 || r.Rewards[0].GetMainItem().GetItemId() != 102) {
					t.Fatalf("missing real rewards: %v", r)
				}
				if attempt == 1 && len(r.Rewards) != 0 {
					t.Fatal("duplicate grant")
				}
			case *proto.PackNotice:
				notices++
			}
		}
		if replies != 1 || notices != 1-attempt {
			t.Fatalf("responses=%d notices=%d", replies, notices)
		}
	}
	if got := p.GetTreasureIllustrationInfo().BookReward; len(got) != 1 || got[0] != 1 {
		t.Fatalf("claim info: %v", got)
	}
	c.bodies = nil
	c.packets = nil
	config.GetGame().Sandbox = false
	g.routeHandle(c, 42, "test", &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 92, MsgId: cmd.TreasureIllustrationRewardReq}, Body: &proto.TreasureIllustrationRewardReq{Id: 2}})
	if len(c.bodies) != 1 || c.bodies[0].(*proto.TreasureIllustrationRewardRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("strict mode must explicitly reject unsupported eligibility")
	}
}
