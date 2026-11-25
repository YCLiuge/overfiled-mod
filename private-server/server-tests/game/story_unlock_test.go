package game

import (
	"github.com/gookit/slog"
	pb "google.golang.org/protobuf/proto"
	"gucooing/lolo/config"
	"gucooing/lolo/game/model"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/cmd"
	"gucooing/lolo/protocol/proto"
	"reflect"
	"testing"
)

func TestSandboxStoryAcceptKeepsCompletion(t *testing.T) {
	oldConf, oldApp := config.CONF, log.App
	defer func() { config.CONF, log.App = oldConf, oldApp }()
	config.CONF = &config.Config{Resources: &config.Resources{ResourcePath: "../Resource", DataPath: "../data"}}
	log.App = slog.NewStdLogger()
	gdconf.LoadGameConfig()
	conn := new(captureConn)
	p := &model.Player{Conn: conn}
	g := new(Game)
	g.newRouter()
	handler := g.handlerFuncRouteMap[cmd.AcceptQuestReq]
	if handler == nil {
		t.Fatal("missing route")
	}
	send := func(body pb.Message) *proto.AcceptQuestRsp {
		conn.bodies, conn.packets = nil, nil
		handler(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 934}, Body: body})
		if len(conn.bodies) != 1 || conn.packets[0] != 934 {
			t.Fatal("bad reply correlation")
		}
		return conn.bodies[0].(*proto.AcceptQuestRsp)
	}
	for _, q := range p.GetQuestDetail().Quests {
		typ := proto.EQuestType(gdconf.GetQuestInfos()[q.QuestId].Config.NewType)
		for i := 0; i < 2; i++ {
			r := send(&proto.AcceptQuestReq{QuestId: q.QuestId, QuestType: typ})
			if r.Status != proto.StatusCode_StatusCode_OK || !reflect.DeepEqual(r.Quest, q) {
				t.Fatalf("completion regressed: %d", q.QuestId)
			}
		}
	}
	for _, body := range []pb.Message{nil, (*proto.AcceptQuestReq)(nil), &proto.PlayerMainDataReq{}, &proto.AcceptQuestReq{}, &proto.AcceptQuestReq{QuestId: ^uint32(0)}, &proto.AcceptQuestReq{QuestId: 110001, QuestType: proto.EQuestType_EQuestType_DailyQuest}} {
		r := send(body)
		if r.Status != proto.StatusCode_StatusCode_BAD_REQ || r.Quest != nil {
			t.Fatal("invalid story accepted")
		}
	}
}
