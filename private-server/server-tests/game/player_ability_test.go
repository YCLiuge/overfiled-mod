package game

import (
	"testing"

	"github.com/gookit/slog"
	pb "google.golang.org/protobuf/proto"
	"gucooing/lolo/config"
	"gucooing/lolo/game/model"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/proto"
)

func TestLocalStarTreeHandlersAndInventory(t *testing.T) {
	oldConf, oldApp, oldGame := config.CONF, log.App, log.Game
	defer func() { config.CONF = oldConf; log.App = oldApp; log.Game = oldGame }()
	config.CONF = &config.Config{Resources: &config.Resources{ResourcePath: "../Resource", DataPath: "../data"}}
	log.App = slog.NewStdLogger()
	log.Game = log.App
	gdconf.LoadGameConfig()
	conn := new(captureConn)
	p := &model.Player{Conn: conn}
	p.ApplySandboxAbilities()
	g := new(Game)
	g.AllPackNotice(p)
	pack := conn.bodies[0].(*proto.PackNotice)
	owned := make(map[uint32]*proto.ItemDetail)
	for _, item := range pack.Items {
		owned[item.MainItem.ItemId] = item
	}
	for _, node := range gdconf.GetAbilityMap() {
		if owned[uint32(node.NeedItemID)] == nil {
			t.Fatal("missing prerequisite")
		}
		for _, level := range node.LevelInfo {
			item := owned[uint32(level.RewardItemID)]
			if item == nil || item.MainItem.ItemTag != proto.EBagItemTag_EBagItemTag_AbilityItem || item.MainItem.GetBaseItem().Num != 1 {
				t.Fatal("missing native level reward")
			}
		}
	}
	g.PlayerAbilityList(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 67}, Body: &proto.PlayerAbilityListReq{}})
	list := conn.bodies[len(conn.bodies)-1].(*proto.PlayerAbilityListRsp)
	if len(list.AbilityList) != 79 || conn.packets[len(conn.packets)-1] != 67 {
		t.Fatal("list response mismatch")
	}
	wire, err := pb.Marshal(list)
	if err != nil || len(wire) == 0 {
		t.Fatal("invalid list wire", err)
	}
	for _, node := range list.AbilityList {
		g.PlayerAbilityLevelUp(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 68}, Body: &proto.PlayerAbilityLevelUpReq{AbilityId: node.AbilityId}})
		rsp := conn.bodies[len(conn.bodies)-1].(*proto.PlayerAbilityLevelUpRsp)
		if rsp.Status != proto.StatusCode_StatusCode_OK || rsp.Ability.Level != node.Level || len(rsp.Items) != 0 {
			t.Fatal("max upgrade did not preserve node")
		}
	}
	g.PlayerAbilityLevelUp(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 69}, Body: &proto.PlayerAbilityLevelUpReq{AbilityId: 0xffffffff}})
	if conn.bodies[len(conn.bodies)-1].(*proto.PlayerAbilityLevelUpRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("invalid node accepted")
	}
}
