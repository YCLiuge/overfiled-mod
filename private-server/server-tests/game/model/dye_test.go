package model

import (
	"encoding/json"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/protocol/proto"
	"math"
	"testing"
)

func TestLocalDyePreviewSaveCancelAndPersistence(t *testing.T) {
	loadGiftTestConfig()
	p := new(Player)
	p.ApplySandboxAppearance()
	const id = uint32(4610231)
	if len(gdconf.GetDye().Colors) != 70 {
		t.Fatalf("real color count: got %d, want 70", len(gdconf.GetDye().Colors))
	}
	param, err := p.SelectOutfitColorant(id, 404)
	if err != nil || param.IsDye || param.PictureId < 1 || param.PictureId > 4 || len(param.Params) != 64 {
		t.Fatalf("select: %v", err)
	}
	c, err := p.PreviewOutfitDye(&proto.OutfitDyeReq{OutfitId: id, Pos: 1})
	if err != nil || c.Red != 255 || c.Green != 135 || c.Blue != 182 {
		t.Fatalf("preview: %v %v", c, err)
	}
	if len(p.Item.GetItemFashionInfo(id).DyeSchemes[0].Colors) != 0 {
		t.Fatal("preview committed")
	}
	before, _ := json.Marshal(p)
	restored := new(Player)
	if err = json.Unmarshal(before, restored); err != nil {
		t.Fatal(err)
	}
	if restored.PendingOutfitDye().Color.Green != 135 {
		t.Fatal("pending not restored")
	}
	_, item, err := restored.SaveOutfitDye(&proto.OutFitDyeSaveReq{OutfitId: id, Pos: 1, IsSaveDyeResult: true})
	if err != nil || item.MainItem.GetOutfit().DyeSchemes[0].Colors[0].Green != 135 || restored.PendingOutfitDye() != nil {
		t.Fatalf("save: %v", err)
	}
	saved, _ := json.Marshal(restored)
	if _, _, err = restored.SaveOutfitDye(&proto.OutFitDyeSaveReq{OutfitId: id, Pos: 1, IsSaveDyeResult: true}); err == nil {
		t.Fatal("duplicate save accepted without a preview")
	}
	duplicate, _ := json.Marshal(restored)
	if string(saved) != string(duplicate) {
		t.Fatal("duplicate save changed state")
	}
	_, err = restored.UnlockOutfitDye(id, 2)
	if err != nil {
		t.Fatal(err)
	}
	unlocked, _ := json.Marshal(restored)
	if _, err = restored.UnlockOutfitDye(id, 2); err != nil {
		t.Fatal(err)
	}
	repeated, _ := json.Marshal(restored)
	if string(unlocked) != string(repeated) {
		t.Fatal("repeat unlock changed state")
	}
	_, err = restored.PreviewOutfitDye(&proto.OutfitDyeReq{OutfitId: id, SchemeIndex: 2, Pos: 0, SpecifyColor: &proto.PosColor{Red: 4, Green: 5, Blue: 6}})
	if err != nil {
		t.Fatal(err)
	}
	_, _, err = restored.SaveOutfitDye(&proto.OutFitDyeSaveReq{OutfitId: id, SchemeIndex: 2, Pos: 0, IsSaveDyeResult: false})
	if err != nil || len(restored.Item.GetItemFashionInfo(id).DyeSchemes[2].Colors) != 0 {
		t.Fatal("cancel committed")
	}
	data, _ := json.Marshal(restored)
	again := new(Player)
	json.Unmarshal(data, again)
	if again.Item.GetItemFashionInfo(id).DyeSchemes[0].Colors[0].Green != 135 || !again.Item.GetItemFashionInfo(id).DyeSchemes[2].IsUnLock {
		t.Fatal("scheme not persisted")
	}
}
func TestLocalDyeRandomUsesRealPalette(t *testing.T) {
	loadGiftTestConfig()
	p := new(Player)
	p.ApplySandboxAppearance()
	const id = uint32(4610231)
	if _, err := p.SelectOutfitColorant(id, 401); err != nil {
		t.Fatal(err)
	}
	for n := 0; n < 32; n++ {
		c, err := p.PreviewOutfitDye(&proto.OutfitDyeReq{OutfitId: id, Pos: 1})
		if err != nil {
			t.Fatal(err)
		}
		found := false
		for _, v := range gdconf.GetDye().Colors {
			if v.NewDyeType != 0 && uint32(v.ColorR) == c.Red && uint32(v.ColorG) == c.Green && uint32(v.ColorB) == c.Blue {
				found = true
				break
			}
		}
		if !found {
			t.Fatalf("random dye outside real palette: %v", c)
		}
	}
}
func TestLocalDyeFailureAtomicityAndChannelReplacement(t *testing.T) {
	loadGiftTestConfig()
	p := new(Player)
	p.ApplySandboxAppearance()
	const id = uint32(4610231)
	p.SelectOutfitColorant(id, 402)
	p.PreviewOutfitDye(&proto.OutfitDyeReq{OutfitId: id, Pos: 1})
	before, _ := json.Marshal(p)
	for _, r := range []*proto.OutfitDyeReq{nil, {OutfitId: 999}, {OutfitId: id, Pos: SandboxDyePositionLimit}, {OutfitId: id, SchemeIndex: SandboxVisibleDyeSchemeCount}, {OutfitId: id, SpecifyColor: &proto.PosColor{Red: 256}}, {OutfitId: id, Uvy: float32(math.NaN())}} {
		if _, err := p.PreviewOutfitDye(r); err == nil {
			t.Fatalf("invalid accepted: %v", r)
		}
		after, _ := json.Marshal(p)
		if string(after) != string(before) {
			t.Fatal("invalid preview changed state")
		}
	}
	for _, r := range []*proto.OutFitDyeSaveReq{nil, {OutfitId: id, Pos: 2}, {OutfitId: 999, Pos: 1}, {OutfitId: id, Pos: 1, SchemeIndex: SandboxVisibleDyeSchemeCount}} {
		if _, _, err := p.SaveOutfitDye(r); err == nil {
			t.Fatal("invalid save accepted")
		}
		after, _ := json.Marshal(p)
		if string(after) != string(before) {
			t.Fatal("invalid save changed state")
		}
	}
	if _, err := p.SelectOutfitColorant(id, 108); err == nil {
		t.Fatal("currency accepted")
	}
	for _, pos := range []uint32{1, 2, 1} {
		p.PreviewOutfitDye(&proto.OutfitDyeReq{OutfitId: id, Pos: pos, SpecifyColor: &proto.PosColor{Red: pos}})
		if _, _, err := p.SaveOutfitDye(&proto.OutFitDyeSaveReq{OutfitId: id, Pos: pos, IsSaveDyeResult: true}); err != nil {
			t.Fatal(err)
		}
	}
	if len(p.Item.GetItemFashionInfo(id).DyeSchemes[0].Colors) != 2 {
		t.Fatal("duplicate channel or lost color")
	}
}
