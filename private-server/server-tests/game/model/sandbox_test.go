package model

import (
	"encoding/json"
	"github.com/gookit/slog"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/log"
	"testing"
)

func TestLocalSandboxUnlockAndRepeat(t *testing.T) {
	loadGiftTestConfig()
	oldGame, oldApp := log.Game, log.App
	log.Game = slog.NewStdLogger()
	log.App = log.Game
	defer func() { log.Game = oldGame; log.App = oldApp }()
	p := new(Player)
	p.ApplySandbox()
	count := 0
	for _, conf := range gdconf.GetCharacterAllMap() {
		if conf == nil || conf.CharacterInfo == nil || !conf.CharacterInfo.IsShow {
			continue
		}
		c := p.GetCharacterModel().GetCharacterInfo(conf.CharacterId)
		if c == nil || !c.IsUnlockPayment {
			t.Fatalf("character not unlocked: %d", conf.CharacterId)
		}
		count++
	}
	if count < 2 {
		t.Fatalf("not enough characters: %d", count)
	}
	for _, id := range []uint32{101, 190, 6020010, 1501} {
		item := p.GetItemModel().GetItemBaseInfo(id)
		if item == nil || item.Num != SandboxResourceFloor {
			t.Fatalf("missing sandbox resource %d: %v", id, item)
		}
	}
	before, _ := json.Marshal(p)
	p.ApplySandbox()
	after, _ := json.Marshal(p)
	if string(before) != string(after) {
		t.Fatal("repeated sandbox login duplicated or changed inventory")
	}
	t.Logf("unlocked %d visible characters; %d base resource records", count, len(p.Item.ItemBaseInfo))
}
