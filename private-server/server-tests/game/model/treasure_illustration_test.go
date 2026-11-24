package model

import (
	"encoding/json"
	"errors"
	"math"
	"reflect"
	"testing"

	"github.com/bytedance/sonic"
	"gucooing/lolo/protocol/proto"
)

func TestLocalTreasureIllustrationResourcesIdempotenceAndJSON(t *testing.T) {
	loadGiftTestConfig()
	p := &Player{}
	var expected int64
	for _, kind := range []proto.EArchaeologyType{proto.EArchaeologyType_EArchaeologyType_Book, proto.EArchaeologyType_EArchaeologyType_Utensil, proto.EArchaeologyType_EArchaeologyType_Shovel} {
		ids := []uint32{1, 2, 3, 4, 5, 6, 7, 8, 9, 10}
		if kind == proto.EArchaeologyType_EArchaeologyType_Shovel {
			ids = []uint32{1007001, 1007002, 1007003}
		}
		for _, id := range ids {
			rewards, pack, err := p.ClaimTreasureIllustration(kind, id)
			if err != nil {
				t.Fatalf("kind=%v id=%d: %v", kind, id, err)
			}
			want := map[uint32]int64{102: 30}
			if kind == proto.EArchaeologyType_EArchaeologyType_Shovel && id == 1007003 {
				want[5070001] = 1
			}
			if got := giftCounts(rewards); !reflect.DeepEqual(got, want) {
				t.Fatalf("kind=%v id=%d gains=%v want=%v", kind, id, got, want)
			}
			expected += 30
			if pack == nil || giftCounts(pack.Items)[102] != expected || p.Item.ItemBaseInfo[102].Num != expected {
				t.Fatal("notice must contain final balance")
			}
			// Exercise the same codec used by player snapshots without touching DB.
			bin, err := sonic.Marshal(p)
			if err != nil {
				t.Fatal(err)
			}
			restored := &Player{}
			if err := sonic.Unmarshal(bin, restored); err != nil {
				t.Fatal(err)
			}
			before, _ := json.Marshal(restored)
			rewards, pack, err = restored.ClaimTreasureIllustration(kind, id)
			if err != nil || len(rewards) != 0 || pack != nil {
				t.Fatalf("duplicate after restore: %v %v %v", rewards, pack, err)
			}
			after, _ := json.Marshal(restored)
			if string(before) != string(after) {
				t.Fatal("duplicate mutated restored player")
			}
			p = restored
		}
	}
	info := p.GetTreasureIllustrationInfo()
	if len(info.BookReward) != 10 || len(info.UtensilReward) != 10 || !reflect.DeepEqual(info.ShovelReward, []uint32{1007001, 1007002, 1007003}) || len(info.TreasureReward) != 0 {
		t.Fatalf("info: %v", info)
	}
	info.BookReward[0] = 999
	if p.TreasureIllustration.BookReward[0] != 1 {
		t.Fatal("info aliases persisted claim IDs")
	}
}

func TestLocalTreasureIllustrationRejectsWithoutMutation(t *testing.T) {
	loadGiftTestConfig()
	for _, tc := range []struct {
		kind proto.EArchaeologyType
		id   uint32
		want error
	}{
		{proto.EArchaeologyType_EArchaeologyType_Book, 0, ErrTreasureIllustrationInvalid},
		{proto.EArchaeologyType_EArchaeologyType_Book, 999999, ErrTreasureIllustrationInvalid},
		{proto.EArchaeologyType_EArchaeologyType_Utensil, 5080001, ErrTreasureIllustrationInvalid},
		{proto.EArchaeologyType_EArchaeologyType_Shovel, 1, ErrTreasureIllustrationInvalid},
		{proto.EArchaeologyType_EArchaeologyType_TreasureMap, 5070002, ErrTreasureIllustrationUnsupported},
		{proto.EArchaeologyType_EArchaeologyType_Furniture, 1, ErrTreasureIllustrationUnsupported},
		{proto.EArchaeologyType(99), 1, ErrTreasureIllustrationUnsupported},
	} {
		p := &Player{}
		rewards, pack, err := p.ClaimTreasureIllustration(tc.kind, tc.id)
		if !errors.Is(err, tc.want) || rewards != nil || pack != nil || p.Item != nil || p.TreasureIllustration != nil {
			t.Fatalf("%+v mutated or wrong error: %v", tc, err)
		}
	}
	var p *Player
	if _, _, err := p.ClaimTreasureIllustration(0, 1); !errors.Is(err, ErrTreasureIllustrationInvalid) {
		t.Fatal(err)
	}
	if p.GetTreasureIllustrationInfo().Status != proto.StatusCode_StatusCode_OK {
		t.Fatal("nil info")
	}
}

func TestLocalTreasureIllustrationAtomicValidation(t *testing.T) {
	cfg := loadGiftTestConfig()
	// The second item in Shovel 1007003 must fail without granting its first.
	for _, tc := range []struct {
		name   string
		change func() func()
		want   error
	}{
		{"missing pool", func() func() {
			old := cfg.Excel.Reward.PoolItems[7000131]
			delete(cfg.Excel.Reward.PoolItems, 7000131)
			return func() { cfg.Excel.Reward.PoolItems[7000131] = old }
		}, ErrTreasureIllustrationConfig},
		{"random quantity", func() func() {
			r := cfg.Excel.Reward.PoolItems[7000131].RewardItemPoolGroup[0]
			old := r.ItemMaxCount
			r.ItemMaxCount++
			return func() { r.ItemMaxCount = old }
		}, ErrTreasureIllustrationUnsupported},
		{"unsupported item", func() func() {
			reward := cfg.Excel.Reward.PoolItems[7000131].RewardItemPoolGroup[0]
			old := reward.ItemID
			reward.ItemID = 1007001
			return func() { reward.ItemID = old }
		}, ErrTreasureIllustrationUnsupported},
	} {
		t.Run(tc.name, func(t *testing.T) {
			restore := tc.change()
			defer restore()
			p := &Player{}
			_, _, err := p.ClaimTreasureIllustration(proto.EArchaeologyType_EArchaeologyType_Shovel, 1007003)
			if !errors.Is(err, tc.want) || p.Item != nil || p.TreasureIllustration != nil {
				t.Fatalf("err=%v player=%+v", err, p)
			}
		})
	}
	p := &Player{Item: &ItemModel{ItemBaseInfo: map[uint32]*ItemBaseInfo{
		102:     {ItemId: 102, ItemType: proto.EBagItemTag_EBagItemTag_Currency, Num: 1, PackType: proto.ItemDetail_PackType_Inventory},
		5070001: {ItemId: 5070001, ItemType: proto.EBagItemTag_EBagItemTag_TreasureMap, Num: math.MaxInt64, PackType: proto.ItemDetail_PackType_Inventory},
	}}}
	before, _ := json.Marshal(p)
	_, _, err := p.ClaimTreasureIllustration(proto.EArchaeologyType_EArchaeologyType_Shovel, 1007003)
	if !errors.Is(err, ErrTreasureIllustrationOverflow) {
		t.Fatal(err)
	}
	after, _ := json.Marshal(p)
	if string(before) != string(after) {
		t.Fatal("overflow partially committed inventory or claim")
	}
}
