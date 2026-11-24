package model

import (
	"encoding/json"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/protocol/proto"
	"math"
	"testing"
)

func TestLocalLifeCraftResources(t *testing.T) {
	loadGiftTestConfig()
	for _, kind := range []proto.LifeType{1, 3, 4, 5} {
		c := gdconf.GetLifeCraftKind(kind)
		if c == nil || c.MaxLevel == 0 || len(c.Recipes) == 0 || len(c.Skills) == 0 {
			t.Fatalf("kind %v incomplete: %+v", kind, c)
		}
		p := &Player{}
		p.ApplySandboxLife()
		if p.LifeBase(kind, true).Level != c.MaxLevel || len(p.LifeSkills(kind, true).LifeSkillInfo) != len(c.Skills) {
			t.Fatal("sandbox level/skills")
		}
		success, fail := 0, 0
		for _, id := range gdconf.GetLifeCraftRecipeIDs(kind) {
			before, _ := json.Marshal(p)
			result, pack, err := p.CraftLife(kind, id, true)
			if err != nil {
				after, _ := json.Marshal(p)
				if string(before) != string(after) {
					t.Fatalf("failed craft mutated %v/%d", kind, id)
				}
				fail++
				continue
			}
			success++
			if result == nil || !result.IsSuccess || len(result.ResultItems) != 1 || pack == nil || len(pack.Items) != 1 || p.Life.Crafted[kind][id] != 1 {
				t.Fatalf("craft %v/%d result/history", kind, id)
			}
		}
		t.Logf("kind %v successful %d rejected %d", kind, success, fail)
		if success == 0 || fail != 0 {
			t.Fatal("visible resource recipes must all be usable")
		}
	}
}
func TestLocalLifeCraftAtomicAndPersisted(t *testing.T) {
	loadGiftTestConfig()
	var kind proto.LifeType = 1
	ids := gdconf.GetLifeCraftRecipeIDs(kind)
	if len(ids) == 0 {
		t.Fatal("missing recipe")
	}
	id := uint32(5012001)
	r := gdconf.GetLifeCraftRecipe(kind, id)
	if r == nil {
		t.Fatal("missing known cooking recipe")
	}
	p := &Player{}
	for _, bad := range []uint32{0, math.MaxUint32} {
		before, _ := json.Marshal(p)
		_, _, err := p.CraftLife(kind, bad, true)
		after, _ := json.Marshal(p)
		if err == nil || string(before) != string(after) {
			t.Fatal("invalid request mutation")
		}
	}
	if _, _, err := p.CraftLife(kind, id, false); err == nil {
		t.Fatal("strict mode unsupported")
	}
	for n := int64(1); n <= 2; n++ {
		result, pack, err := p.CraftLife(kind, id, true)
		if err != nil {
			t.Fatal(err)
		}
		if giftCounts(result.ResultItems)[uint32(r.GetItemID)] != 1 || giftCounts(pack.Items)[uint32(r.GetItemID)] != n {
			t.Fatal("reward delta or pack absolute")
		}
	}
	raw, _ := json.Marshal(p)
	restored := &Player{}
	if err := json.Unmarshal(raw, restored); err != nil {
		t.Fatal(err)
	}
	if restored.Life.Crafted[kind][id] != 2 {
		t.Fatal("history lost")
	}
	counts := restored.LifePictorialCounts(kind)
	if len(counts) != 4 || counts[0].Id != 150120011 || counts[0].Count != 2 || len(restored.LifeAchievements(kind).LifeAchieve) != 4 {
		t.Fatalf("achievement IDs/counts were not restored: %v", counts)
	}
	restored.Life.Crafted[kind][id] = math.MaxUint32
	beforeCount, _ := json.Marshal(restored)
	if _, _, err := restored.CraftLife(kind, id, true); err == nil {
		t.Fatal("history overflow accepted")
	}
	afterCount, _ := json.Marshal(restored)
	if string(beforeCount) != string(afterCount) {
		t.Fatal("history overflow mutation")
	}
	restored.Life.Crafted[kind][id] = 2
	restored.Item.ItemBaseInfo[uint32(r.GetItemID)].Num = math.MaxInt64
	before, _ := json.Marshal(restored)
	if _, _, err := restored.CraftLife(kind, id, true); err == nil {
		t.Fatal("overflow accepted")
	}
	after, _ := json.Marshal(restored)
	if string(before) != string(after) {
		t.Fatal("overflow mutation")
	}
}
