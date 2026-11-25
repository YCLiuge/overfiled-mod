package model

import (
	"bytes"
	"encoding/json"
	pb "google.golang.org/protobuf/proto"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/protocol/proto"
	"testing"
)

func TestAbilityBadgeSandboxCatalogAndPersistence(t *testing.T) {
	loadGiftTestConfig()
	p := new(Player)
	p.GetArchive().SetArchiveMap("existing.client.key", "preserve")
	info, err := p.AbilityBadgeInfo(0, true)
	if err != nil {
		t.Fatal(err)
	}
	if len(info.AbilityBadgePages) != 8 {
		t.Fatalf("pages=%d", len(info.AbilityBadgePages))
	}
	total := 0
	for _, page := range info.AbilityBadgePages {
		area := badgeArea(page.Page)
		if area == nil || len(page.ActiveBoxIds) != int(area.UnlockCount) {
			t.Fatalf("page %d count=%d want=%d", page.Page, len(page.ActiveBoxIds), area.GetUnlockCount())
		}
		total += len(page.ActiveBoxIds)
		for _, n := range badgeNodes(page.Page) {
			if validBadgeNode(n) != badgeContains(page.ActiveBoxIds, uint32(n.BoxID)) {
				t.Fatalf("page %d box %d placeholder/activation mismatch", page.Page, n.BoxID)
			}
		}
	}
	if total != 198 {
		t.Fatalf("active boxes=%d", total)
	}
	if len(info.AbilityBadgeAchieves) == 0 {
		t.Fatal("missing moonstone achievements")
	}
	for _, a := range info.AbilityBadgeAchieves {
		c := gdconf.GetAchieveConfigure(a.AchieveId)
		if c.NewFunctionOfAchievement != 9 || a.Count != uint64(c.CountParam) {
			t.Fatalf("incorrect achievement %v", a)
		}
	}
	wire, err := pb.Marshal(info)
	if err != nil {
		t.Fatal(err)
	}
	decoded := new(proto.AbilityBadgeListRsp)
	if err := pb.Unmarshal(wire, decoded); err != nil || !pb.Equal(decoded, info) {
		t.Fatal("wire roundtrip", err)
	}
	before, _ := json.Marshal(p)
	if _, err := p.ActivateAbilityBadge(1, 1, true); err != nil {
		t.Fatal(err)
	}
	if _, err := p.AbilityBadgeInfo(0, true); err != nil {
		t.Fatal(err)
	}
	after, _ := json.Marshal(p)
	if !bytes.Equal(before, after) {
		t.Fatal("repeat changed save")
	}
	restored := new(Player)
	if err := json.Unmarshal(before, restored); err != nil {
		t.Fatal(err)
	}
	got, err := restored.AbilityBadgeInfo(0, true)
	if err != nil || !pb.Equal(got, info) {
		t.Fatal("reload lost state", err)
	}
	if restored.Archive.ArchiveMap["existing.client.key"] != "preserve" {
		t.Fatal("overwrote other archive")
	}
	single, err := restored.AbilityBadgeInfo(1, true)
	if err != nil || len(single.AbilityBadgePages) != 1 || single.AbilityBadgePages[0].Page != 1 {
		t.Fatal("scene filtering", err)
	}
	// Client slices must not alias persistent state.
	single.AbilityBadgePages[0].ActiveBoxIds[0] = 9999
	again, _ := restored.AbilityBadgeInfo(1, false)
	if again.AbilityBadgePages[0].ActiveBoxIds[0] == 9999 {
		t.Fatal("list leaked mutable state")
	}
	t.Logf("pages=%d active_boxes=%d achievements=%d", len(info.AbilityBadgePages), total, len(info.AbilityBadgeAchieves))
}
func TestAbilityBadgeRejectsWithoutMutation(t *testing.T) {
	loadGiftTestConfig()
	p := new(Player)
	before, _ := json.Marshal(p)
	for _, req := range []struct {
		page, box uint32
		sandbox   bool
	}{{1, 1, false}, {999, 1, true}, {1, 999, true}, {1, 3, true}, {0, 1, true}} {
		if _, err := p.ActivateAbilityBadge(req.page, req.box, req.sandbox); err == nil {
			t.Fatalf("accepted invalid request %+v", req)
		}
	}
	if _, err := p.AbilityBadgeInfo(999, true); err == nil {
		t.Fatal("accepted invalid scene")
	}
	after, _ := json.Marshal(p)
	if !bytes.Equal(before, after) {
		t.Fatal("invalid request mutated player")
	}
	info, err := p.AbilityBadgeInfo(1, false)
	if err != nil || len(info.AbilityBadgePages) != 1 || len(info.AbilityBadgePages[0].ActiveBoxIds) != 0 {
		t.Fatal("ordinary player got sandbox completion", err)
	}
	after, _ = json.Marshal(p)
	if !bytes.Equal(before, after) {
		t.Fatal("read mutated player")
	}
	p.GetArchive().SetArchiveMap(abilityBadgeArchiveKey, "{bad")
	before, _ = json.Marshal(p)
	if _, err := p.AbilityBadgeInfo(1, true); err == nil {
		t.Fatal("silently discarded corrupt state")
	}
	after, _ = json.Marshal(p)
	if !bytes.Equal(before, after) {
		t.Fatal("corrupt save overwritten")
	}
}
func TestAbilityBadgePageIDDiffersFromSceneID(t *testing.T) {
	loadGiftTestConfig()
	area := badgeArea(1)
	oldScene := area.SceneID
	area.SceneID = 777
	defer func() { area.SceneID = oldScene }()
	p := new(Player)
	info, err := p.AbilityBadgeInfo(777, true)
	if err != nil || len(info.AbilityBadgePages) != 1 || info.AbilityBadgePages[0].Page != 1 {
		t.Fatal("network page must use area.ID and filter must use SceneID", err)
	}
	if _, err := p.ActivateAbilityBadge(1, 1, true); err != nil {
		t.Fatal(err)
	}
	if _, err := p.ActivateAbilityBadge(777, 1, true); err == nil {
		t.Fatal("accepted scene as network page")
	}
}

func TestAbilityBadgeIsolatedDatabaseRoundTrip(t *testing.T) {
	loadGiftTestConfig()
	openPlayerTestDB(t)
	p := &Player{UserId: 900001}
	want, err := p.AbilityBadgeInfo(0, true)
	if err != nil {
		t.Fatal(err)
	}
	if err := p.FlushPlayer(); err != nil {
		t.Fatal(err)
	}
	restored := readPlayer(t, 900001)
	got, err := restored.AbilityBadgeInfo(0, false)
	if err != nil {
		t.Fatal(err)
	}
	// Sandbox achievement completion is a response policy; box state is persisted.
	if !pb.Equal(&proto.AbilityBadgeListRsp{AbilityBadgePages: want.AbilityBadgePages}, &proto.AbilityBadgeListRsp{AbilityBadgePages: got.AbilityBadgePages}) {
		t.Fatal("SQL snapshot lost badge page state")
	}
}

func TestAbilityBadgeMigrationPreservesClaims(t *testing.T) {
	loadGiftTestConfig()
	p := new(Player)
	p.storeBadgeSave(&abilityBadgeSave{Pages: map[uint32]*proto.AbilityBadgePageInfo{1: {Page: 1, IsReward: true, ActiveBoxIds: []uint32{1, 2}}}, Rewards: []uint32{50101010}})
	info, err := p.AbilityBadgeInfo(1, true)
	if err != nil || !info.AbilityBadgePages[0].IsReward || !badgeContains(info.AbilityBadgeRewardAchieveIds, 50101010) {
		t.Fatal("migration dropped claim history", err)
	}
}
