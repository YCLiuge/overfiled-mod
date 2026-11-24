package game

import (
	"github.com/gookit/slog"
	"gucooing/lolo/config"
	"gucooing/lolo/game/model"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/proto"
	"testing"
	"time"
)

func TestLocalFishingHandlerTickCancelAndLifeUI(t *testing.T) {
	oldConfig, oldLog := config.CONF, log.App
	defer func() { config.CONF = oldConfig; log.App = oldLog }()
	config.CONF = &config.Config{Resources: &config.Resources{ResourcePath: "../Resource", DataPath: "../data"}, Game: &config.Game{Sandbox: true}}
	log.App = slog.NewStdLogger()
	gdconf.LoadGameConfig()
	c := &captureConn{}
	p := &model.Player{UserId: 1, Online: true, Conn: c, Item: &model.ItemModel{ItemWeaponMap: map[uint32]*model.ItemWeaponInfo{}}}
	for id, v := range gdconf.GetWeaponAllMap() {
		if v != nil && v.WeaponInfo != nil && v.WeaponInfo.NewWeaponType == 18 {
			p.Item.ItemWeaponMap[7] = &model.ItemWeaponInfo{WeaponId: id, InstanceId: 7}
			break
		}
	}
	g := &Game{}
	g.getWordInfo().allScenePlayer.Store(uint32(1), &ScenePlayer{Player: p, CurScene: &model.ScenePlayerInfo{Player: p, SceneId: 1}})
	call := func(body *proto.FishingReq) {
		g.Fishing(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 9}, Body: body})
	}
	req := &proto.FishingReq{FishingRodId: 7, FishingPondId: 1001}
	call(req)
	if c.packets[0] != 9 || c.bodies[0].(*proto.FishingRsp).Status != proto.StatusCode_StatusCode_OK {
		t.Fatal("cast did not reply")
	}
	g.TickFishing(p, time.Now().Add(time.Minute))
	if len(c.bodies) != 3 {
		t.Fatalf("pack/result notices missing: %d", len(c.bodies))
	}
	if _, ok := c.bodies[1].(*proto.PackNotice); !ok {
		t.Fatal("pack first")
	}
	if c.bodies[2].(*proto.FishingResultNotice).Status != proto.StatusCode_StatusCode_OK || p.Fishing.Total != 1 {
		t.Fatal("tick did not settle")
	}
	call(req)
	call(&proto.FishingReq{IsCancel: true})
	n := len(c.bodies)
	g.TickFishing(p, time.Now().Add(time.Hour))
	if len(c.bodies) != n || p.Fishing.Total != 1 {
		t.Fatal("cancel tick emitted reward")
	}
	call(req)
	p.Online = false
	g.TickFishing(p, time.Now().Add(time.Hour))
	if p.Fishing.Pending != nil || p.Fishing.Total != 1 {
		t.Fatal("offline reward")
	}
	p.Online = true
	g.GetLifeInfo(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 10}, Body: &proto.GetLifeInfoReq{LifeType: 2}})
	info := c.bodies[len(c.bodies)-1].(*proto.GetLifeInfoRsp)
	if info.Status != proto.StatusCode_StatusCode_OK || info.LifeBaseInfo.Level != 10 || len(info.LifeSkill.LifeSkillInfo) != 6 {
		t.Fatal("fishing UI unsupported")
	}
}
