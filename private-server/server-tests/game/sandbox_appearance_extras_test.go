package game

import (
	"encoding/json"
	"path/filepath"
	"testing"

	"github.com/gookit/slog"
	pb "google.golang.org/protobuf/proto"
	"gucooing/lolo/config"
	"gucooing/lolo/db"
	"gucooing/lolo/game/model"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/proto"
)

func TestLocalSandboxAppearanceExtrasEquip(t *testing.T) {
	oldConf, oldApp, oldGame := config.CONF, log.App, log.Game
	defer func() { config.CONF = oldConf; log.App = oldApp; log.Game = oldGame }()
	config.CONF = &config.Config{Resources: &config.Resources{ResourcePath: "../Resource", DataPath: "../data"}}
	log.App = slog.NewStdLogger()
	log.Game = log.App
	gdconf.LoadGameConfig()
	if err := db.NewDB(&db.Option{Type: db.Sqlite, Dsn: filepath.Join(t.TempDir(), "appearance.sqlite"), MaxIdleConns: 1, MaxOpenConns: 1, PersistShardNum: 1, PersistBufSize: 2}); err != nil {
		t.Fatal(err)
	}
	defer db.Close()
	conn := new(captureConn)
	p := &model.Player{UserId: 724101, Conn: conn}
	p.ApplySandbox()
	p.ApplySandboxAppearanceExtras()
	if _, err := db.GetGameBasicOrCreate(p.UserId); err != nil {
		t.Fatal(err)
	}
	g := new(Game)
	ch := &ChannelInfo{serverSceneSyncChan: make(chan *ServerSceneSyncCtx, 4)}
	sp := &ScenePlayer{Player: p, channelInfo: ch}
	g.getWordInfo().allScenePlayer.Store(p.UserId, sp)
	charID := p.GetTeamModel().GetTeamInfo().Char1
	if charID == 0 {
		charID = 101001
		p.GetTeamModel().GetTeamInfo().Char1 = charID
	}
	c := p.GetCharacterModel().GetCharacterInfo(charID)
	sp.CurScene = &model.ScenePlayerInfo{Player: p, Team: p.GetTeamModel().GetTeamInfo()}
	ch.sceneServerDatas = make(map[uint32]*proto.ServerSceneSyncData)
	// Every catalog item must reach the actual login inventory notification with
	// its correct oneof; tools and vehicles carry real WeaponInstance identifiers.
	conn.bodies = nil
	conn.packets = nil
	g.AllPackNotice(p)
	notice := conn.bodies[0].(*proto.PackNotice)
	if conn.packets[0] != 0 {
		t.Fatal("inventory notice must be asynchronous")
	}
	bases := map[uint32]bool{}
	weapons := map[uint32]uint32{}
	for _, d := range notice.Items {
		if d.GetMainItem().GetBaseItem() != nil {
			bases[d.GetMainItem().GetBaseItem().GetItemId()] = true
		}
		if w := d.GetMainItem().GetWeapon(); w != nil {
			if w.InstanceId == 0 {
				t.Fatal("zero tool instance")
			}
			weapons[w.WeaponId] = w.InstanceId
		}
	}
	cat := gdconf.GetAppearanceExtras()
	headCounts := map[uint32]int{}
	for _, d := range notice.Items {
		if d.GetMainItem().GetItemTag() == proto.EBagItemTag_EBagItemTag_Head {
			id := d.GetMainItem().GetItemId()
			if d.GetMainItem().GetBaseItem().GetNum() != 1 || d.GetPackType() != proto.ItemDetail_PackType_Inventory {
				t.Fatal("avatar login quantity/pack invalid")
			}
			headCounts[id]++
		}
	}
	for id := range cat.Heads {
		if headCounts[id] != 1 {
			t.Fatalf("avatar %d missing or duplicated in login inventory", id)
		}
	}
	for id := range cat.Base {
		if !bases[id] {
			t.Fatalf("catalog base %d absent from PackNotice", id)
		}
	}
	for id := range cat.Weapons {
		if weapons[id] == 0 {
			t.Fatalf("catalog tool %d absent from PackNotice", id)
		}
	}
	conn.bodies = nil
	conn.packets = nil
	g.UnlockHeadList(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 815}, Body: &proto.UnlockHeadListReq{}})
	headRsp := conn.bodies[0].(*proto.UnlockHeadListRsp)
	heads := map[uint32]bool{}
	for _, id := range headRsp.Heads {
		heads[id] = true
	}
	if conn.packets[0] != 815 {
		t.Fatal("head list packet mismatch")
	}
	for id := range cat.Heads {
		if !heads[id] {
			t.Fatalf("configured head %d not sent", id)
		}
	}
	conn.bodies, conn.packets = nil, nil
	g.sendOwnedHeads(p, 0)
	pushed := conn.bodies[0].(*proto.UnlockHeadListRsp)
	wire, err := pb.Marshal(pushed)
	if err != nil {
		t.Fatal(err)
	}
	round := new(proto.UnlockHeadListRsp)
	if err = pb.Unmarshal(wire, round); err != nil || round.Status != proto.StatusCode_StatusCode_OK || conn.packets[0] != 0 || len(round.Heads) != len(cat.Heads) {
		t.Fatal("login avatar-unlock push malformed")
	}
	for _, id := range round.Heads {
		if !heads[id] {
			t.Fatal("login push differs from requested unlock list")
		}
	}
	t.Logf("network extras base=%d weapons=%d heads=%d", len(cat.Base), len(cat.Weapons), len(cat.Heads))
	syncOK := func(character uint32) {
		t.Helper()
		select {
		case ctx := <-ch.serverSceneSyncChan:
			if ctx.ScenePlayer != sp || ctx.CharacterId != character || ctx.ActionType != proto.SceneActionType_SceneActionType_UPDATE_APPEARANCE {
				t.Fatal("wrong scene sync")
			}
			ch.sceneServerDatas = make(map[uint32]*proto.ServerSceneSyncData)
			ch.serverSceneSync(ctx)
			data := ch.sceneServerDatas[p.UserId].ServerData[0]
			if !pb.Equal(data.Player.GetTeam().GetChar1().GetCharacterAppearance(), c.GetPbCharacterAppearance()) {
				t.Fatal("scene sync lost actual character appearance")
			}
		default:
			t.Fatal("missing scene sync")
		}
	}
	noSync := func() {
		t.Helper()
		select {
		case <-ch.serverSceneSyncChan:
			t.Fatal("failed request synchronized")
		default:
		}
	}
	sendChar := func(a *proto.CharacterAppearance) *proto.UpdateCharacterAppearanceRsp {
		conn.bodies = nil
		conn.packets = nil
		g.UpdateCharacterAppearance(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 812}, Body: &proto.UpdateCharacterAppearanceReq{CharId: charID, Appearance: a}})
		if len(conn.bodies) != 1 || conn.packets[0] != 812 {
			t.Fatal("character response packet mismatch")
		}
		return conn.bodies[0].(*proto.UpdateCharacterAppearanceRsp)
	}
	basePick := map[proto.EBagItemTag]uint32{}
	for id, tag := range cat.Base {
		basePick[tag] = id
	}
	badge, umbrella := basePick[proto.EBagItemTag_EBagItemTag_Badge], basePick[proto.EBagItemTag_EBagItemTag_Umbrella]
	pendant, frame := basePick[proto.EBagItemTag_EBagItemTag_Pendant], basePick[proto.EBagItemTag_EBagItemTag_AvatarFrame]
	if badge == 0 || umbrella == 0 || pendant == 0 || frame == 0 {
		t.Fatal("empty appearance category")
	}
	c.CharacterAppearance = nil
	rsp := sendChar(&proto.CharacterAppearance{Badge: badge, UmbrellaId: umbrella})
	if rsp.Status != proto.StatusCode_StatusCode_OK || rsp.Appearance.Badge != badge || rsp.Appearance.UmbrellaId != umbrella {
		t.Fatalf("base wear: %v", rsp)
	}
	syncOK(charID)
	basic, _ := db.GetGameBasic(p.UserId)
	if basic.TeamLeaderBadge != badge {
		t.Fatal("leader badge not persisted")
	}
	setters := map[int32]func(*proto.CharacterAppearance, uint32){
		4: func(a *proto.CharacterAppearance, id uint32) { a.CollectionGlovesInstanceId = id }, 5: func(a *proto.CharacterAppearance, id uint32) { a.LoggingAxeInstanceId = id }, 6: func(a *proto.CharacterAppearance, id uint32) { a.MiningHammerInstanceId = id }, 7: func(a *proto.CharacterAppearance, id uint32) { a.InsectNetInstanceId = id }, 8: func(a *proto.CharacterAppearance, id uint32) { a.WaterBottleInstanceId = id }, 18: func(a *proto.CharacterAppearance, id uint32) { a.FishingRodInstanceId = id }, 21: func(a *proto.CharacterAppearance, id uint32) { a.VehicleInstanceId = id }, 22: func(a *proto.CharacterAppearance, id uint32) { a.ShovelInstanceId = id },
	}
	seen := map[int32]bool{}
	for id, w := range cat.Weapons {
		kind := int32(w.NewWeaponType)
		if kind == int32(proto.EWeaponType_EWeaponType_Unarmed) {
			// Empty hand uses the held-tool request, not an appearance slot.
			g.CharacterGatherWeaponUpdate(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 819}, Body: &proto.CharacterGatherWeaponUpdateReq{CharacterId: charID, WeaponId: weapons[id]}})
			if conn.packets[len(conn.packets)-1] != 819 || conn.bodies[len(conn.bodies)-1].(*proto.CharacterGatherWeaponUpdateRsp).Status != proto.StatusCode_StatusCode_OK || c.GatherWeapon != id {
				t.Fatal("empty-hand tool could not be selected")
			}
			select {
			case ctx := <-ch.serverSceneSyncChan:
				if ctx.CharacterId != charID || ctx.ActionType != proto.SceneActionType_SceneActionType_UPDATE_EQUIP {
					t.Fatal("empty-hand selection did not synchronize")
				}
				ch.sceneServerDatas = make(map[uint32]*proto.ServerSceneSyncData)
				ch.serverSceneSync(ctx)
				if ch.sceneServerDatas[p.UserId].ServerData[0].Player.GetTeam().GetChar1().GetGatherWeapon() != id {
					t.Fatal("scene lost selected empty-hand tool")
				}
			default:
				t.Fatal("missing empty-hand scene sync")
			}
			continue
		}
		set := setters[kind]
		if set == nil {
			t.Fatalf("unexpected weapon category %d", kind)
		}
		if seen[kind] {
			continue
		}
		seen[kind] = true
		for _, value := range []uint32{weapons[id], id} {
			a := &proto.CharacterAppearance{}
			set(a, value)
			rsp := sendChar(a)
			want := c.GetPbCharacterAppearance()
			if rsp.Status != proto.StatusCode_StatusCode_OK || !pb.Equal(rsp.Appearance, want) {
				t.Fatalf("tool type%d id%d: %v", kind, value, rsp)
			}
			got := &proto.CharacterAppearance{}
			set(got, value)
			fd := got.ProtoReflect().Descriptor().Fields()
			for i := 0; i < fd.Len(); i++ {
				f := fd.Get(i)
				if got.ProtoReflect().Get(f).Uint() != 0 && rsp.Appearance.ProtoReflect().Get(f).Uint() != uint64(value) {
					t.Fatalf("equipped tool value lost")
				}
			}
			if rsp.Appearance.Badge != badge || rsp.Appearance.UmbrellaId != umbrella {
				t.Fatal("patch erased unrelated appearance")
			}
			syncOK(charID)
		}
	}
	if len(seen) != 8 {
		t.Fatalf("expected 8 tool categories, got %d", len(seen))
	}
	before, _ := json.Marshal(c.CharacterAppearance)
	for _, a := range []*proto.CharacterAppearance{nil, {Badge: umbrella}, {UmbrellaId: badge}, {Badge: badge, VehicleInstanceId: 4294967295}, {MiningHammerInstanceId: pendant}} {
		if r := sendChar(a); r.Status != proto.StatusCode_StatusCode_BAD_REQ {
			t.Fatalf("invalid wear accepted: %v", r)
		}
		noSync()
		after, _ := json.Marshal(c.CharacterAppearance)
		if string(before) != string(after) {
			t.Fatal("failed request changed appearance")
		}
	}
	// Zero remains a no-op in the existing character patch protocol.
	if r := sendChar(&proto.CharacterAppearance{}); r.Status != proto.StatusCode_StatusCode_OK {
		t.Fatal(r)
	}
	syncOK(charID)
	after, _ := json.Marshal(c.CharacterAppearance)
	if string(before) != string(after) {
		t.Fatal("zero patch erased appearance")
	}
	sendPlayer := func(a *proto.PlayerAppearance) *proto.UpdatePlayerAppearanceRsp {
		conn.bodies = nil
		conn.packets = nil
		g.UpdatePlayerAppearance(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 813}, Body: &proto.UpdatePlayerAppearanceReq{Appearance: a}})
		if len(conn.bodies) != 1 || conn.packets[0] != 813 {
			t.Fatal("player response packet mismatch")
		}
		return conn.bodies[0].(*proto.UpdatePlayerAppearanceRsp)
	}
	r := sendPlayer(&proto.PlayerAppearance{Pendant: pendant, AvatarFrame: frame})
	if r.Status != proto.StatusCode_StatusCode_OK || r.Appearance.Pendant != pendant || r.Appearance.AvatarFrame != frame {
		t.Fatal(r)
	}
	syncOK(0)
	for _, a := range []*proto.PlayerAppearance{nil, {Pendant: badge}, {AvatarFrame: pendant}, {Pendant: pendant, AvatarFrame: 4294967295}} {
		if r := sendPlayer(a); r.Status != proto.StatusCode_StatusCode_BAD_REQ {
			t.Fatal("invalid player wear accepted")
		}
		noSync()
		b, _ := db.GetGameBasic(p.UserId)
		if b.Pendant != pendant || b.AvatarFrame != frame {
			t.Fatal("failed player request changed DB")
		}
	}
	r = sendPlayer(&proto.PlayerAppearance{})
	if r.Status != proto.StatusCode_StatusCode_OK || r.Appearance.Pendant != 0 || r.Appearance.AvatarFrame != 0 {
		t.Fatal("player clear failed")
	}
	syncOK(0)
	// Genuine config without ownership must fail, even outside sandbox.
	saved := p.Item
	p.Item = &model.ItemModel{}
	if r := sendChar(&proto.CharacterAppearance{Badge: badge}); r.Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("unowned badge accepted")
	}
	noSync()
	if r := sendPlayer(&proto.PlayerAppearance{Pendant: pendant}); r.Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("unowned pendant accepted")
	}
	noSync()
	p.Item = saved
	conn.bodies = nil
	g.UpdateCharacterAppearance(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 814}, Body: &proto.UpdateCharacterAppearanceReq{CharId: 999, Appearance: &proto.CharacterAppearance{Badge: badge}}})
	if conn.bodies[0].(*proto.UpdateCharacterAppearanceRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("unknown character accepted")
	}
	noSync()
}
