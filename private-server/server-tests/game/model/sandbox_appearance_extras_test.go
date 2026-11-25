package model

import (
	"bytes"
	"encoding/json"
	"os"
	"testing"

	pb "google.golang.org/protobuf/proto"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/protocol/excel"
	"gucooing/lolo/protocol/proto"
)

func TestLocalAppearanceExtrasCatalogAndWire(t *testing.T) {
	loadGiftTestConfig()
	a := gdconf.GetAppearanceExtras()
	if a == nil {
		t.Fatal("extras catalog not loaded")
	}
	expected := map[string]int{"badge": 253, "umbrella": 240, "pendant": 38, "avatarFrame": 62, "phoneCase": 31, "head": 207, "unarmed": 1, "collectionGloves": 8, "loggingAxe": 9, "miningHammer": 9, "insectNet": 9, "waterBottle": 9, "fishingRod": 10, "vehicle": 19, "shovel": 3}
	p := new(Player)
	p.ApplySandboxAppearanceExtras()
	for category, n := range expected {
		ids := a.Categories[category]
		if len(ids) != n {
			t.Fatalf("%s expected %d got %d", category, n, len(ids))
		}
		t.Logf("%s=%d", category, len(ids))
		for _, id := range ids {
			if !p.OwnsAppearanceExtra(category, id) {
				t.Fatalf("not owned: %s %d", category, id)
			}
			item := gdconf.GetItemConfigure(id)
			if item == nil {
				t.Fatalf("missing Item row %d", id)
			}
			var detail *proto.ItemDetail
			switch {
			case a.Weapons[id] != nil:
				c := a.Weapons[id]
				if gdconf.AppearanceExtraWeaponCategory(proto.EWeaponType(c.NewWeaponType)) != category || c.ID != c.ItemID || c.AssetFullName == "" || c.WeaponPropertyID <= 0 {
					t.Fatalf("invalid weapon relation %d", id)
				}
				for _, w := range p.Item.ItemWeaponMap {
					if w.ItemId == id {
						detail = w.ItemDetail()
						if !p.OwnsAppearanceExtra(category, w.InstanceId) {
							t.Fatal("instance not owned")
						}
						break
					}
				}
				if detail == nil || detail.MainItem.GetWeapon() == nil || detail.MainItem.ItemTag != proto.EBagItemTag_EBagItemTag_Weapon || detail.MainItem.GetWeapon().InstanceId == 0 {
					t.Fatalf("wrong weapon wire %s %d", category, id)
				}
			case a.Heads[id]:
				detail = p.Item.ItemHeadMap[id].ItemDetail()
				if detail.MainItem.GetBaseItem().Num != 1 {
					t.Fatal("owned avatar must have positive wire quantity")
				}
			default:
				detail = p.Item.ItemBaseInfo[id].ItemDetail()
				if detail.MainItem.GetBaseItem() == nil || detail.MainItem.GetBaseItem().Num != 1 || detail.MainItem.ItemTag != a.Base[id] {
					t.Fatalf("wrong base wire %d", id)
				}
			}
			wire, err := pb.Marshal(&proto.PackNotice{Status: proto.StatusCode_StatusCode_OK, Items: []*proto.ItemDetail{detail}})
			if err != nil {
				t.Fatal(err)
			}
			round := new(proto.PackNotice)
			if err = pb.Unmarshal(wire, round); err != nil || !pb.Equal(round.Items[0], detail) {
				t.Fatalf("wire roundtrip %d: %v", id, err)
			}
		}
	}
	if a.Heads[41204] {
		t.Fatal("orphan Item avatar without Head configuration granted")
	}
	// Independently traverse Phone and Item rather than the derived catalog.
	phoneRaw, err := os.ReadFile("../../Resource/Excel/Phone.json")
	if err != nil {
		t.Fatal(err)
	}
	phones := new(excel.AllPhoneDatas)
	if err = json.Unmarshal(phoneRaw, phones); err != nil {
		t.Fatal(err)
	}
	for _, row := range phones.GetPhone().GetDatas() {
		id := uint32(row.ID)
		item := gdconf.GetItemConfigure(id)
		if item == nil || proto.EBagItemTag(item.NewBagItemTag) != proto.EBagItemTag_EBagItemTag_PhoneCase || !p.OwnsAppearanceExtra("phoneCase", id) {
			t.Fatalf("configured phone case excluded or invalid: %d", id)
		}
	}
	if p.OwnsAppearanceExtra("phoneCase", 9005) || p.OwnsAppearanceExtra("head", 7000) {
		t.Fatal("phone/head categories mixed")
	}
	// Independently traverse raw Weapon rather than trusting catalog's own filter.
	raw, err := os.ReadFile("../../Resource/Excel/Weapon.json")
	if err != nil {
		t.Fatal(err)
	}
	w := new(excel.AllWeaponDatas)
	if err = json.Unmarshal(raw, w); err != nil {
		t.Fatal(err)
	}
	n := 0
	for _, r := range w.GetWeapon().GetDatas() {
		if gdconf.AppearanceExtraWeaponCategory(proto.EWeaponType(r.NewWeaponType)) != "" {
			n++
			if a.Weapons[uint32(r.ItemID)] == nil {
				t.Fatalf("raw tool excluded %d", r.ID)
			}
		}
	}
	if n != 77 {
		t.Fatalf("expected 77 actual tool/vehicle weapon rows, got %d", n)
	}
	if p.OwnsAppearanceExtra("badge", 4000) || p.OwnsAppearanceExtra("vehicle", 1007001) || p.OwnsAppearanceExtra("missing", 4000) || p.OwnsAppearanceExtra("umbrella", 0) {
		t.Fatal("wrong category accepted")
	}
}

func TestLocalAppearanceExtrasIdempotentAndJSON(t *testing.T) {
	loadGiftTestConfig()
	p := new(Player)
	// Existing instances, worn state, upgrade values and head acquisition times
	// must survive both the grant and a serialized login cycle.
	p.ApplySandboxAppearanceExtras()
	var existing *ItemWeaponInfo
	for _, w := range p.Item.ItemWeaponMap {
		existing = w
		break
	}
	if existing == nil {
		t.Fatal("no tools")
	}
	existing.WearerId = 4101
	existing.WearerIndex = 2
	existing.StrengthLevel = 7
	firstHead := p.Item.ItemHeadMap[9005]
	if firstHead == nil {
		t.Fatal("requested shop avatar 9005 missing")
	}
	firstHead.AddTime = 123
	p.Item.ItemBaseInfo[4000].Num = 17
	before, err := json.Marshal(p)
	if err != nil {
		t.Fatal(err)
	}
	p.ApplySandboxAppearanceExtras()
	after, _ := json.Marshal(p)
	if !bytes.Equal(before, after) {
		t.Fatal("repeat grant changed ownership or equipment")
	}
	restored := new(Player)
	if err = json.Unmarshal(before, restored); err != nil {
		t.Fatal(err)
	}
	restored.ApplySandboxAppearanceExtras()
	final, _ := json.Marshal(restored)
	if !bytes.Equal(before, final) {
		t.Fatal("JSON roundtrip/login changed extras")
	}
	if restored.Item.ItemWeaponMap[existing.InstanceId].StrengthLevel != 7 || restored.Item.ItemHeadMap[9005].AddTime != 123 {
		t.Fatal("existing customization overwritten")
	}
	untouched := new(Player)
	if untouched.OwnsAppearanceExtra("umbrella", 4000) || untouched.Item != nil {
		t.Fatal("ownership query mutated player")
	}
}
