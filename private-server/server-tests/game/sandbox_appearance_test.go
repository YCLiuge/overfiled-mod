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
)

func TestLocalSandboxAppearanceWearAndAction(t *testing.T) {
	oldConf, oldApp, oldGame := config.CONF, log.App, log.Game
	defer func() { config.CONF = oldConf; log.App = oldApp; log.Game = oldGame }()
	config.CONF = &config.Config{Resources: &config.Resources{ResourcePath: "../Resource", DataPath: "../data"}}
	log.App = slog.NewStdLogger()
	log.Game = log.App
	gdconf.LoadGameConfig()
	conn := new(captureConn)
	p := &model.Player{UserId: 42, Conn: conn}
	p.ApplySandbox()
	g := new(Game)
	msg := &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 71}, Body: &proto.OutfitPresetUpdateReq{CharId: 101001, Preset: &proto.OutfitPreset{PresetIndex: 0, Hat: 4610231, PendTop: 4641003}}}
	g.OutfitPresetUpdate(p, msg)
	rsp := conn.bodies[0].(*proto.OutfitPresetUpdateRsp)
	if rsp.Status != proto.StatusCode_StatusCode_OK || rsp.Preset.Hat != 4610231 || rsp.Preset.PendTop != 4641003 || conn.packets[0] != 71 {
		t.Fatalf("wear request failed: %v", rsp)
	}
	c := p.GetCharacterModel().GetCharacterInfo(101001)
	if c.GetOutfitPreset(0).Hat != 4610231 || c.GetOutfitPreset(0).PendTop != 4641003 {
		t.Fatal("wear not persisted in character preset")
	}
	for _, req := range []*proto.OutfitPresetUpdateReq{{CharId: 101001}, {CharId: 999, Preset: &proto.OutfitPreset{}}, {CharId: 101001, Preset: &proto.OutfitPreset{PresetIndex: gdconf.GetConstant().OutfitPresetNum}}} {
		conn.bodies = nil
		msg.Body = req
		g.OutfitPresetUpdate(p, msg)
		if conn.bodies[0].(*proto.OutfitPresetUpdateRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
			t.Fatal("malformed outfit falsely succeeded")
		}
	}
	conn.bodies = nil
	msg.Body = &proto.OutfitPresetSwitchReq{CharId: 101001, UsePresetIndex: 0}
	g.OutfitPresetSwitch(p, msg)
	if conn.bodies[0].(*proto.OutfitPresetSwitchRsp).Status != proto.StatusCode_StatusCode_OK || c.InUseOutfitPresetIndex != 0 {
		t.Fatal("preset switch failed")
	}
	conn.bodies = nil
	conn.packets = nil
	g.SandboxActionUnlockNotice(p)
	notice := conn.bodies[0].(*proto.AchieveActionUnLockNotice)
	if len(notice.ActionIdLst) == 0 || conn.packets[0] != 0 {
		t.Fatal("missing asynchronous action unlock notice")
	}
	channel := &ChannelInfo{actionSyncChan: make(chan *ActionSyncCtx, 1)}
	sp := &ScenePlayer{Player: p, channelInfo: channel}
	g.getWordInfo().allScenePlayer.Store(p.UserId, sp)
	conn.bodies = nil
	msg.Body = &proto.SendActionReq{ActionId: notice.ActionIdLst[0]}
	g.SendAction(p, msg)
	if conn.bodies[0].(*proto.SendActionRsp).Status != proto.StatusCode_StatusCode_OK {
		t.Fatal("action failed")
	}
	select {
	case action := <-channel.actionSyncChan:
		if action.ActionId != notice.ActionIdLst[0] || action.ScenePlayer != sp {
			t.Fatal("wrong action dispatched")
		}
	default:
		t.Fatal("action was not queued for room sync")
	}
}
