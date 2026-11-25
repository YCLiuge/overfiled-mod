package model

import (
	"bytes"
	"encoding/json"
	"testing"

	pb "google.golang.org/protobuf/proto"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/protocol/proto"
)

func TestLocalStarTreeFullCatalogPersistence(t *testing.T) {
	loadGiftTestConfig()
	p := new(Player)
	if len(p.PlayerAbilities()) != 0 {
		t.Fatal("ordinary new player got unlocked nodes")
	}
	p.ApplySandboxAbilities()
	nodes, rewards := gdconf.GetAbilityMap(), 0
	if len(nodes) != 79 {
		t.Fatalf("catalog nodes: %d", len(nodes))
	}
	for id, node := range nodes {
		info := p.PlayerAbility(id)
		if info == nil {
			t.Fatalf("missing node %d", id)
		}
		var max uint32
		for _, level := range node.LevelInfo {
			rewards++
			item := gdconf.GetItemConfigure(uint32(level.RewardItemID))
			effect := gdconf.GetPlayerAbilityConfigure(level.RewardItemID)
			if item == nil || effect == nil || proto.EBagItemTag(item.NewBagItemTag) != proto.EBagItemTag_EBagItemTag_AbilityItem {
				t.Fatalf("invalid reward/effect %d", level.RewardItemID)
			}
			owned := p.Item.GetItemBaseInfo(uint32(level.RewardItemID))
			if owned == nil || owned.Num != 1 {
				t.Fatalf("missing level reward %d", level.RewardItemID)
			}
			if uint32(level.Level) > max {
				max = uint32(level.Level)
			}
		}
		if info.Level != max {
			t.Fatalf("node %d: %d want %d", id, info.Level, max)
		}
		if _, items, ok := p.LevelUpPlayerAbility(id); !ok || len(items) != 0 {
			t.Fatal("max node not idempotent")
		}
	}
	if rewards != 389 {
		t.Fatalf("rewards: %d", rewards)
	}
	wire, err := pb.Marshal(&proto.PlayerAbilityListRsp{Status: proto.StatusCode_StatusCode_OK, AbilityList: p.PlayerAbilities()})
	if err != nil {
		t.Fatal(err)
	}
	decoded := new(proto.PlayerAbilityListRsp)
	if err := pb.Unmarshal(wire, decoded); err != nil || len(decoded.AbilityList) != 79 {
		t.Fatal("wire nodes missing", err)
	}
	before, _ := json.Marshal(p)
	p.ApplySandboxAbilities()
	after, _ := json.Marshal(p)
	if !bytes.Equal(before, after) {
		t.Fatal("repeat grant changed persisted data")
	}
	restored := new(Player)
	if err := json.Unmarshal(before, restored); err != nil {
		t.Fatal(err)
	}
	if !pb.Equal(&proto.PlayerAbilityListRsp{AbilityList: p.PlayerAbilities()}, &proto.PlayerAbilityListRsp{AbilityList: restored.PlayerAbilities()}) {
		t.Fatal("levels lost on reload")
	}
	restored.ApplySandboxAbilities()
	again, _ := json.Marshal(restored)
	if !bytes.Equal(before, again) {
		t.Fatal("reload migration duplicated rewards")
	}
	t.Logf("nodes=%d level_rewards=%d", len(nodes), rewards)
}

func TestLocalStarTreePartialSaveAndUpgrade(t *testing.T) {
	loadGiftTestConfig()
	p := new(Player)
	// Node 1010 has 3 ordered levels and native unlock item 102000001.
	if _, _, ok := p.LevelUpPlayerAbility(1010); ok {
		t.Fatal("locked node upgraded")
	}
	p.GetItemModel().AddItemBase(102000001, 1)
	p.GetItemModel().AddItemBase(103, 10)
	got, items, ok := p.LevelUpPlayerAbility(1010)
	if !ok || got.Level != 1 || len(items) != 2 || p.Item.GetItemBaseInfo(103).Num != 0 {
		t.Fatal("first upgrade failed")
	}
	if _, _, ok := p.LevelUpPlayerAbility(1010); ok {
		t.Fatal("unfunded upgrade allowed")
	}
	if p.PlayerAbility(1010).Level != 1 {
		t.Fatal("failed request changed level")
	}
	reward := p.Item.GetItemBaseInfo(110001011)
	reward.Num = 7
	p.ApplySandboxAbilities()
	if p.PlayerAbility(1010).Level != 3 || reward.Num != 7 {
		t.Fatal("migration lost existing quantity/level")
	}
	before, _ := json.Marshal(p)
	if _, _, ok := p.LevelUpPlayerAbility(0xffffffff); ok {
		t.Fatal("unknown node accepted")
	}
	after, _ := json.Marshal(p)
	if !bytes.Equal(before, after) {
		t.Fatal("bad node mutated save")
	}
}
