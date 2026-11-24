package model

import (
	"encoding/json"
	"testing"

	"github.com/gookit/slog"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/excel"
)

func TestSandboxWuCharacterUnlockAndRestore(t *testing.T) {
	loadGiftTestConfig()
	oldGame := log.Game
	log.Game = slog.NewStdLogger()
	defer func() { log.Game = oldGame }()
	const wuID uint32 = 202004 // String.json: character NameID 202004 is 乌.
	conf := gdconf.GetCharacterAll(wuID)
	if conf == nil || conf.CharacterInfo == nil || conf.CharacterInfo.NameID != int32(wuID) {
		t.Fatal("Wu character configuration missing or identity changed")
	}
	if conf.CharacterInfo.IsShow {
		t.Fatal("fixture no longer exercises Wu's hidden presentation flag")
	}
	p := new(Player)
	if p.AddCharacter(wuID) != nil {
		t.Fatal("normal acquisition must retain hidden-character restriction")
	}
	p.ApplySandbox()
	c := p.GetCharacterModel().GetCharacterInfo(wuID)
	if c == nil || !c.IsUnlockPayment {
		t.Fatal("sandbox did not grant and unlock Wu")
	}
	preset := c.GetEquipmentPreset(c.InUseEquipmentPresetIndex)
	weapon := p.GetItemModel().GetItemWeaponInfo(preset.WeaponInstanceId)
	if weapon == nil || weapon.ItemId != uint32(conf.CharacterInfo.DefaultWeaponID) {
		t.Fatal("Wu's configured default weapon was not initialized")
	}
	outfit := c.GetOutfitPreset(c.InUseOutfitPresetIndex)
	if outfit.Hat != uint32(conf.CharacterInfo.HatID) || outfit.Hair != uint32(conf.CharacterInfo.HairID) || outfit.Clothes != uint32(conf.CharacterInfo.ClothID) {
		t.Fatal("Wu's configured default outfit was not initialized")
	}
	for _, skill := range conf.CharacterInfo.SpellIDs {
		if c.CharacterSkillList[uint32(skill)] == nil {
			t.Fatalf("Wu skill %d not initialized", skill)
		}
	}
	found := false
	for _, character := range p.GetCharacterModel().GetAllPbCharacter() {
		if character.CharacterId == wuID {
			found = character.IsUnlockPayment
		}
	}
	if !found {
		t.Fatal("Wu missing from character response")
	}
	// Exercise persisted ownership and a later login without touching a database.
	c.Level = 17
	c.IsNew = false
	before, err := json.Marshal(p)
	if err != nil {
		t.Fatal(err)
	}
	var restored Player
	if err := json.Unmarshal(before, &restored); err != nil {
		t.Fatal(err)
	}
	restored.ApplySandbox()
	after, err := json.Marshal(&restored)
	if err != nil {
		t.Fatal(err)
	}
	if string(before) != string(after) {
		t.Fatal("repeated sandbox login changed persisted character or inventory")
	}
	for _, row := range gdconf.GetCharacterAllMap() {
		if row != nil && row.CharacterInfo != nil && !row.CharacterInfo.IsShow && row.CharacterId != wuID && restored.GetCharacterModel().GetCharacterInfo(row.CharacterId) != nil {
			t.Fatalf("unverified hidden character %d was granted", row.CharacterId)
		}
	}
}

func TestSandboxCharacterAvailability(t *testing.T) {
	for _, tc := range []struct {
		name string
		conf *gdconf.CharacterAllInfo
		want bool
	}{
		{"nil row", nil, false},
		{"level rules only", &gdconf.CharacterAllInfo{CharacterId: sandboxWuCharacterID}, false},
		{"visible", &gdconf.CharacterAllInfo{CharacterId: 1, CharacterInfo: &excel.CharacterConfigure{IsShow: true}}, true},
		{"unverified hidden", &gdconf.CharacterAllInfo{CharacterId: 1, CharacterInfo: &excel.CharacterConfigure{}}, false},
		{"verified Wu", &gdconf.CharacterAllInfo{CharacterId: sandboxWuCharacterID, CharacterInfo: &excel.CharacterConfigure{}}, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			if got := sandboxCharacterAvailable(tc.conf); got != tc.want {
				t.Fatalf("availability=%v, want %v", got, tc.want)
			}
		})
	}
}
