package model

import (
	"encoding/json"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/protocol/excel"
	"gucooing/lolo/protocol/proto"
	"os"
	"testing"
)

func TestLocalSandboxAppearanceRealCatalog(t *testing.T) {
	loadGiftTestConfig()
	p := new(Player)
	p.ApplySandboxAppearance()
	// Alternate body-model rows may share one canonical wearable item. Verify
	// every positive fashion ItemID in the raw resource has actual ownership.
	raw, err := os.ReadFile("../../Resource/Excel/Fashion.json")
	if err != nil {
		t.Fatal(err)
	}
	all := new(excel.AllFashionDatas)
	if err = json.Unmarshal(raw, all); err != nil {
		t.Fatal(err)
	}
	for _, row := range all.GetFashionInfo().GetDatas() {
		if row == nil || row.ItemID <= 0 {
			continue
		}
		item := gdconf.GetItemConfigure(uint32(row.ItemID))
		if item == nil || proto.EBagItemTag(item.NewBagItemTag) != proto.EBagItemTag_EBagItemTag_Fashion {
			continue
		}
		if p.Item.GetItemFashionInfo(uint32(row.ItemID)) == nil {
			t.Fatalf("raw wearable absent: model %d item %d", row.ID, row.ItemID)
		}
	}
	fashionCount := 0
	for id, c := range gdconf.GetFashionAllMap() {
		if c == nil || c.FashionInfo == nil {
			continue
		}
		item := gdconf.GetItemConfigure(uint32(c.FashionInfo.ItemID))
		if item == nil || proto.EBagItemTag(item.NewBagItemTag) != proto.EBagItemTag_EBagItemTag_Fashion {
			continue
		}
		f := p.Item.GetItemFashionInfo(id)
		if f == nil || f.OutfitId != id || f.ItemId != uint32(c.FashionInfo.ItemID) || f.DyeSchemes[0] == nil || !f.DyeSchemes[0].IsUnLock {
			t.Fatalf("invalid owned fashion %d: %+v", id, f)
		}
		detail := f.ItemDetail()
		if detail.MainItem.GetOutfit().OutfitId != id {
			t.Fatalf("fashion wire ID %d", id)
		}
		fashionCount++
	}
	for _, id := range []uint32{4610231, 4641003} {
		if p.Item.GetItemFashionInfo(id) == nil {
			t.Fatalf("reported shop fashion missing: %d", id)
		}
	}
	for id := range gdconf.GetChatEmotionMap() {
		if id > 0 && p.Chat.UnLockExpression[uint32(id)] == nil {
			t.Fatalf("chat emotion not unlocked %d", id)
		}
	}
	a := gdconf.GetAppearance()
	if a == nil {
		t.Fatal("appearance catalog not loaded")
	}
	notice := p.SandboxActionUnlockNotice()
	if len(notice.ActionIdLst) != len(a.UnlockActions) || len(notice.ActionIdLst) == 0 {
		t.Fatal("incomplete action notice")
	}
	for n, id := range notice.ActionIdLst {
		if a.Actions[id] == nil {
			t.Fatalf("action has no animation %d", id)
		}
		if n > 0 && notice.ActionIdLst[n-1] >= id {
			t.Fatal("action notification not ordered and unique")
		}
	}
	// Existing user colors are preserved even when login grants the rest of the catalog.
	f := p.Item.GetItemFashionInfo(4610231)
	f.DyeSchemes[0].Colors = Colors{&PosColor{Pos: 1, Red: 7, Green: 8, Blue: 9}}
	before, _ := json.Marshal(p)
	p.ApplySandboxAppearance()
	after, _ := json.Marshal(p)
	if string(before) != string(after) {
		t.Fatal("repeat appearance grant changed existing dyes or duplicated inventory")
	}
	restored := new(Player)
	if err := json.Unmarshal(before, restored); err != nil {
		t.Fatal(err)
	}
	restored.ApplySandboxAppearance()
	roundtrip, _ := json.Marshal(restored)
	if string(before) != string(roundtrip) {
		t.Fatal("appearance ownership not persistent/idempotent")
	}
	t.Logf("%d fashions, %d expressions/stickers, %d configured public actions", fashionCount, len(p.Chat.UnLockExpression), len(notice.ActionIdLst))
}
