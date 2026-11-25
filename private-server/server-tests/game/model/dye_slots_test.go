package model

import (
	"encoding/json"
	"gucooing/lolo/protocol/proto"
	"testing"
)

func TestLocalFiveDyeSlotsMigrateAndPersist(t *testing.T) {
	loadGiftTestConfig()
	p := new(Player)
	const id uint32 = 4610231
	p.ApplySandboxAppearance()
	f := p.GetItemModel().GetItemFashionInfo(id)
	// Reproduce a legacy save with only slot zero and a historical extra slot.
	f.DyeSchemes = map[uint32]*OutfitDyeScheme{
		0: {SchemeIndex: 0, IsUnLock: true, Colors: Colors{&PosColor{Pos: 1, Red: 71}}},
		1: {SchemeIndex: 1, Colors: Colors{&PosColor{Pos: 1, Green: 93}}},
		7: {SchemeIndex: 7, IsUnLock: true, Colors: Colors{&PosColor{Pos: 1, Blue: 82}}},
	}
	p.ApplySandboxAppearance()
	for index := uint32(0); index < SandboxVisibleDyeSchemeCount; index++ {
		if f.DyeSchemes[index] == nil || !f.DyeSchemes[index].IsUnLock {
			t.Fatalf("slot %d is locked after legacy migration", index)
		}
		color := &proto.PosColor{Red: index + 10, Green: index + 20, Blue: index + 30}
		if _, err := p.PreviewOutfitDye(&proto.OutfitDyeReq{OutfitId: id, SchemeIndex: index, Pos: 2, SpecifyColor: color}); err != nil {
			t.Fatalf("slot %d preview: %v", index, err)
		}
		if _, _, err := p.SaveOutfitDye(&proto.OutFitDyeSaveReq{OutfitId: id, SchemeIndex: index, Pos: 2, IsSaveDyeResult: true}); err != nil {
			t.Fatalf("slot %d save: %v", index, err)
		}
	}
	if f.DyeSchemes[0].Colors[0].Red != 71 || f.DyeSchemes[1].Colors[0].Green != 93 || f.DyeSchemes[7].Colors[0].Blue != 82 {
		t.Fatal("migration erased existing colors")
	}
	before, err := json.Marshal(p)
	if err != nil {
		t.Fatal(err)
	}
	restored := new(Player)
	if err = json.Unmarshal(before, restored); err != nil {
		t.Fatal(err)
	}
	restored.ApplySandboxAppearance()
	after, err := json.Marshal(restored)
	if err != nil {
		t.Fatal(err)
	}
	if string(before) != string(after) {
		t.Fatal("relogin migration changed persisted dye data")
	}
	schemes := restored.GetItemModel().GetItemFashionInfo(id).ItemDetail().MainItem.GetOutfit().DyeSchemes
	for index := uint32(0); index < SandboxVisibleDyeSchemeCount; index++ {
		scheme := schemes[index]
		if scheme.SchemeIndex != index || !scheme.IsUnLock {
			t.Fatalf("wire order/unlock incorrect at %d", index)
		}
		found := false
		for _, color := range scheme.Colors {
			if color.Pos == 2 && color.Red == index+10 {
				found = true
			}
		}
		if !found {
			t.Fatalf("slot %d lost its independent saved color", index)
		}
	}
}
