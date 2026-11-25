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
	"gucooing/lolo/protocol/proto"
	"testing"
)

func TestLocalDyeHandlerFlow(t *testing.T) {
	oldConf, oldApp, oldGame := config.CONF, log.App, log.Game
	defer func() { config.CONF = oldConf; log.App = oldApp; log.Game = oldGame }()
	config.CONF = &config.Config{Resources: &config.Resources{ResourcePath: "../Resource", DataPath: "../data"}, Game: &config.Game{Sandbox: true}}
	log.App = slog.NewStdLogger()
	log.Game = log.App
	gdconf.LoadGameConfig()
	conn := new(captureConn)
	p := &model.Player{UserId: 42, Conn: conn}
	p.ApplySandbox()
	g := new(Game)
	send := func(body pb.Message, fn func(*model.Player, *alg.GameMsg)) {
		conn.bodies = nil
		conn.packets = nil
		fn(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 97}, Body: body})
	}
	send(&proto.OutfitDyeUnlockIndexReq{OutfitId: 4610231, Index: 2}, g.OutfitDyeUnlockIndex)
	if len(conn.bodies) != 2 || conn.bodies[0].(*proto.PackNotice).Items[0].MainItem.GetOutfit().OutfitId != 4610231 || conn.packets[0] != 0 || conn.packets[1] != 97 || conn.bodies[1].(*proto.OutfitDyeUnlockIndexRsp).Status != proto.StatusCode_StatusCode_OK {
		t.Fatalf("unlock flow: %v", conn.bodies)
	}
	send(&proto.OutfitColorantSelectReq{OutfitId: 4610231, ColorantItemId: 404}, g.OutfitColorantSelect)
	selected := conn.bodies[0].(*proto.OutfitColorantSelectRsp)
	if selected.Status != proto.StatusCode_StatusCode_OK || conn.packets[0] != 97 {
		t.Fatal("select failed")
	}
	wire, err := pb.Marshal(selected)
	if err != nil {
		t.Fatal(err)
	}
	decoded := new(proto.OutfitColorantSelectRsp)
	if err = pb.Unmarshal(wire, decoded); err != nil {
		t.Fatal(err)
	}
	if decoded.Param == nil || decoded.Param.IsDye || len(decoded.Param.Params) != 64 || decoded.Param.PictureId != 1 {
		t.Fatal("picker cannot initialize from selection response")
	}
	for i, v := range decoded.Param.Params {
		if v != v || v < -0.8 || v > 0.8 || (i%4 != 2 && v < 0.2) {
			t.Fatalf("invalid picker vector component %d: %v", i, v)
		}
	}
	send(&proto.OutfitDyeReq{OutfitId: 4610231, SchemeIndex: 2, Pos: 1}, g.OutfitDye)
	if conn.bodies[0].(*proto.OutfitDyeRsp).PosColor.Green != 135 {
		t.Fatal("preview failed")
	}
	send(&proto.OutFitDyeSaveReq{OutfitId: 4610231, SchemeIndex: 2, Pos: 1, IsSaveDyeResult: true}, g.OutFitDyeSave)
	if len(conn.bodies) != 2 || conn.packets[0] != 0 || conn.packets[1] != 97 || conn.bodies[1].(*proto.OutFitDyeSaveRsp).Status != proto.StatusCode_StatusCode_OK {
		t.Fatalf("save flow: %v", conn.bodies)
	}
	send(&proto.OutfitPresetUpdateReq{CharId: 101001, Preset: &proto.OutfitPreset{Hat: 4610231, HatDyeSchemeIndex: 2}}, g.OutfitPresetUpdate)
	if conn.bodies[0].(*proto.OutfitPresetUpdateRsp).Status != proto.StatusCode_StatusCode_OK {
		t.Fatal("wear dyed scheme failed")
	}
	c := p.GetCharacterModel().GetCharacterInfo(101001)
	before, _ := json.Marshal(c)
	for _, scheme := range []uint32{model.SandboxVisibleDyeSchemeCount, model.SandboxDyeSchemeLimit} {
		send(&proto.OutfitPresetUpdateReq{CharId: 101001, Preset: &proto.OutfitPreset{Hat: 4610231, HatDyeSchemeIndex: scheme}}, g.OutfitPresetUpdate)
		if conn.bodies[0].(*proto.OutfitPresetUpdateRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
			t.Fatal("invalid scheme worn")
		}
		after, _ := json.Marshal(c)
		if string(after) != string(before) {
			t.Fatal("invalid preset mutated")
		}
	}
	send(&proto.OutfitPresetSwitchReq{CharId: 101001}, g.OutfitPresetSwitch)
	if c.GetOutfitPreset(c.InUseOutfitPresetIndex).HatDyeSchemeIndex != 2 {
		t.Fatal("switched scheme lost")
	}
	data, _ := json.Marshal(p)
	restored := new(model.Player)
	json.Unmarshal(data, restored)
	if restored.GetItemModel().GetItemFashionInfo(4610231).DyeSchemes[2].Colors[0].Green != 135 || restored.GetCharacterModel().GetCharacterInfo(101001).GetOutfitPreset(0).HatDyeSchemeIndex != 2 {
		t.Fatal("dye wear not persistent")
	}
	bad := c.GetOutfitPreset(1)
	bad.Hat = 4610231
	bad.HatDyeSchemeIndex = 9
	before, _ = json.Marshal(c)
	send(&proto.OutfitPresetSwitchReq{CharId: 101001, UsePresetIndex: 1}, g.OutfitPresetSwitch)
	if conn.bodies[0].(*proto.OutfitPresetSwitchRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("historical bad scheme switched")
	}
	afterSwitch, _ := json.Marshal(c)
	if string(afterSwitch) != string(before) {
		t.Fatal("invalid switch mutated")
	}
	// All five visible slots must work without a manual unlock request,
	// including the formerly locked second through fifth choices.
	for index := uint32(0); index < model.SandboxVisibleDyeSchemeCount; index++ {
		send(&proto.OutfitDyeReq{OutfitId: 4610231, SchemeIndex: index, Pos: 1, SpecifyColor: &proto.PosColor{Red: 20 + index}}, g.OutfitDye)
		if conn.bodies[0].(*proto.OutfitDyeRsp).Status != proto.StatusCode_StatusCode_OK {
			t.Fatalf("visible slot %d preview failed", index)
		}
		send(&proto.OutFitDyeSaveReq{OutfitId: 4610231, SchemeIndex: index, Pos: 1, IsSaveDyeResult: true}, g.OutFitDyeSave)
		if len(conn.bodies) != 2 || conn.bodies[1].(*proto.OutFitDyeSaveRsp).Status != proto.StatusCode_StatusCode_OK {
			t.Fatalf("visible slot %d save failed", index)
		}
		send(&proto.OutfitPresetUpdateReq{CharId: 101001, Preset: &proto.OutfitPreset{Hat: 4610231, HatDyeSchemeIndex: index}}, g.OutfitPresetUpdate)
		if conn.bodies[0].(*proto.OutfitPresetUpdateRsp).Status != proto.StatusCode_StatusCode_OK {
			t.Fatalf("visible slot %d wear failed", index)
		}
	}
	config.CONF.Game.Sandbox = false
	before, _ = json.Marshal(p)
	send(&proto.OutfitDyeReq{OutfitId: 4610231, SchemeIndex: 2, Pos: 1}, g.OutfitDye)
	if conn.bodies[0].(*proto.OutfitDyeRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("strict fabricated dye")
	}
	after, _ := json.Marshal(p)
	if string(after) != string(before) {
		t.Fatal("strict mutation")
	}
}
