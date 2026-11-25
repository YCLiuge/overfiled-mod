package model

import (
	"encoding/json"
	"testing"

	"gucooing/lolo/gdconf"
	"gucooing/lolo/protocol/proto"
)

func TestLocalSandboxUnarmedMigratesOldInventory(t *testing.T) {
	loadGiftTestConfig()
	const unarmed uint32 = 1001000
	conf := gdconf.GetAppearanceExtras()
	if len(conf.Categories["unarmed"]) != 1 || conf.Categories["unarmed"][0] != unarmed {
		t.Fatal("canonical empty-hand tool missing")
	}
	p := new(Player)
	p.ApplySandboxAppearanceExtras()
	// Reproduce an old inventory with every previously supported tool but no
	// empty-hand item, then grant again as during an existing account login.
	for id, item := range p.Item.ItemWeaponMap {
		if item.ItemId == unarmed {
			delete(p.Item.ItemWeaponMap, id)
		}
	}
	oldCount := len(p.Item.ItemWeaponMap)
	p.ApplySandboxAppearanceExtras()
	if len(p.Item.ItemWeaponMap) != oldCount+1 {
		t.Fatal("old account did not receive exactly one empty-hand instance")
	}
	var instance uint32
	for id, item := range p.Item.ItemWeaponMap {
		if item.ItemId == unarmed {
			instance = id
			wire := item.ItemDetail().MainItem.GetWeapon()
			if wire == nil || item.WeaponId != unarmed || item.ItemType != proto.EBagItemTag_EBagItemTag_Weapon || wire.InstanceId != id {
				t.Fatal("invalid empty-hand wire item")
			}
		}
	}
	if instance == 0 || !p.OwnsAppearanceExtra("unarmed", instance) || !p.OwnsAppearanceExtra("unarmed", unarmed) {
		t.Fatal("empty-hand ownership missing")
	}
	if p.OwnsAppearanceExtra("collectionGloves", instance) {
		t.Fatal("empty-hand tool misclassified as collection gloves")
	}
	before, err := json.Marshal(p)
	if err != nil {
		t.Fatal(err)
	}
	restored := new(Player)
	if err = json.Unmarshal(before, restored); err != nil {
		t.Fatal(err)
	}
	restored.ApplySandboxAppearanceExtras()
	if len(restored.Item.ItemWeaponMap) != oldCount+1 || restored.Item.GetItemWeaponInfo(instance).WeaponId != unarmed {
		t.Fatal("relogin duplicated or replaced empty-hand instance")
	}
	after, _ := json.Marshal(restored)
	if string(before) != string(after) {
		t.Fatal("relogin changed inventory")
	}
}
