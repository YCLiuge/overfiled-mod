package model

import (
	"encoding/json"
	"errors"
	"math"
	"reflect"
	"sync"
	"testing"

	"github.com/gookit/slog"
	"gucooing/lolo/config"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/proto"
)

var giftConfigOnce sync.Once
var giftTestConfig *gdconf.GameConfig

func loadGiftTestConfig() *gdconf.GameConfig {
	giftConfigOnce.Do(func() {
		oldConfig, oldLog := config.CONF, log.App
		defer func() { config.CONF, log.App = oldConfig, oldLog }()
		config.CONF = &config.Config{Resources: &config.Resources{ResourcePath: "../../Resource", DataPath: "../../data"}}
		log.App = slog.NewStdLogger()
		giftTestConfig = gdconf.LoadGameConfig()
	})
	return giftTestConfig
}
func giftPlayer(id uint32, num int64) *Player {
	return &Player{Item: &ItemModel{ItemBaseInfo: map[uint32]*ItemBaseInfo{
		id: {ItemId: id, Num: num, ItemType: proto.EBagItemTag_EBagItemTag_Gift, PackType: proto.ItemDetail_PackType_Inventory},
	}}}
}
func giftCounts(items []*proto.ItemDetail) map[uint32]int64 {
	m := map[uint32]int64{}
	for _, item := range items {
		m[item.MainItem.ItemId] = item.MainItem.GetBaseItem().Num
	}
	return m
}
func TestLocalUseDailyGiftResourcesAndRepeat(t *testing.T) {
	loadGiftTestConfig()
	for _, id := range []uint32{6020010, 6010040} {
		p := giftPlayer(id, 5)
		for run := int64(1); run <= 2; run++ {
			rewards, pack, err := p.UseGift(id, 2, 0)
			if err != nil {
				t.Fatal(err)
			}
			want := map[uint32]int64{190: 4, 101: 2000}
			if id == 6020010 {
				want[1501] = 4
			}
			if got := giftCounts(rewards); !reflect.DeepEqual(got, want) {
				t.Fatalf("gift %d rewards %v want %v", id, got, want)
			}
			final := giftCounts(pack.Items)
			if final[id] != 5-run*2 {
				t.Fatalf("gift balance %v", final)
			}
			for reward, count := range want {
				if final[reward] != run*count {
					t.Fatalf("reward balance %v", final)
				}
			}
		}
		before, _ := json.Marshal(p.Item)
		_, _, err := p.UseGift(id, 2, 0)
		if !errors.Is(err, ErrGiftNotEnough) {
			t.Fatalf("want not enough: %v", err)
		}
		after, _ := json.Marshal(p.Item)
		if string(before) != string(after) {
			t.Fatal("failed use mutated inventory")
		}
	}
}
func TestLocalUseSandboxGiftWithoutInventoryAndRepeat(t *testing.T) {
	loadGiftTestConfig()
	for _, p := range []*Player{{}, {Item: &ItemModel{}}, giftPlayer(6020010, 0), giftPlayer(6020010, 7)} {
		var original *ItemBaseInfo
		if p.Item != nil {
			original = p.Item.ItemBaseInfo[6020010]
		}
		for run := int64(1); run <= 2; run++ {
			rewards, pack, err := p.UseSandboxGift(6020010, 3, 0)
			if err != nil {
				t.Fatal(err)
			}
			want := map[uint32]int64{190: 6, 101: 3000, 1501: 6}
			if got := giftCounts(rewards); !reflect.DeepEqual(got, want) {
				t.Fatalf("sandbox gains: %v", got)
			}
			if _, ok := giftCounts(pack.Items)[6020010]; ok {
				t.Fatal("sandbox announced source gift mutation")
			}
			if p.Item.ItemBaseInfo[6020010] != original {
				t.Fatal("sandbox created/replaced source gift")
			}
			if original != nil && original.Num != 0 && original.Num != 7 {
				t.Fatal("sandbox consumed source gift")
			}
			for id, gain := range want {
				if p.Item.ItemBaseInfo[id].Num != run*gain {
					t.Fatalf("sandbox balance for %d", id)
				}
			}
		}
	}
}

func TestLocalUseSandboxGiftFailureDoesNotMutate(t *testing.T) {
	loadGiftTestConfig()
	for _, p := range []*Player{{}, {Item: &ItemModel{ItemBaseInfo: map[uint32]*ItemBaseInfo{101: {ItemId: 101, Num: math.MaxInt64, ItemType: proto.EBagItemTag_EBagItemTag_Currency}}}}} {
		before, _ := json.Marshal(p.Item)
		var err error
		if p.Item == nil {
			_, _, err = p.UseSandboxGift(4294967295, 1, 0)
		} else {
			_, _, err = p.UseSandboxGift(6020010, 1, 0)
		}
		if err == nil {
			t.Fatal("sandbox invalid use succeeded")
		}
		after, _ := json.Marshal(p.Item)
		if string(before) != string(after) {
			t.Fatal("sandbox failure mutated inventory")
		}
	}
}

func TestLocalUseGiftAggregateOverflowDoesNotMutate(t *testing.T) {
	c := loadGiftTestConfig()
	pool := c.Excel.Reward.PoolItems[6020010]
	r := pool.RewardItemPoolGroup[0]
	oldMin, oldMax, oldRows := r.ItemMinCount, r.ItemMaxCount, pool.RewardItemPoolGroup
	defer func() { r.ItemMinCount, r.ItemMaxCount, pool.RewardItemPoolGroup = oldMin, oldMax, oldRows }()
	r.ItemMinCount, r.ItemMaxCount = math.MaxInt32, math.MaxInt32
	pool.RewardItemPoolGroup = append(append(pool.RewardItemPoolGroup[:0:0], pool.RewardItemPoolGroup...), r)
	p := giftPlayer(6020010, math.MaxInt64)
	before, _ := json.Marshal(p.Item)
	_, _, err := p.UseGift(6020010, math.MaxUint32, 0)
	if !errors.Is(err, ErrGiftOverflow) {
		t.Fatalf("aggregate overflow: %v", err)
	}
	after, _ := json.Marshal(p.Item)
	if string(before) != string(after) {
		t.Fatal("overflow mutated inventory")
	}
}

func TestLocalUseGiftIncompleteTablesDoNotPanic(t *testing.T) {
	c := loadGiftTestConfig()
	for _, table := range []string{"gift", "item", "reward"} {
		t.Run(table, func(t *testing.T) {
			gift, item, reward := c.Excel.GiftBag, c.Excel.Item, c.Excel.Reward
			defer func() { c.Excel.GiftBag, c.Excel.Item, c.Excel.Reward = gift, item, reward }()
			switch table {
			case "gift":
				c.Excel.GiftBag = nil
			case "item":
				c.Excel.Item = nil
			case "reward":
				c.Excel.Reward = nil
			}
			p := giftPlayer(6020010, 5)
			before, _ := json.Marshal(p.Item)
			if _, _, err := p.UseGift(6020010, 1, 0); !errors.Is(err, ErrGiftConfig) {
				t.Fatalf("incomplete table: %v", err)
			}
			after, _ := json.Marshal(p.Item)
			if string(before) != string(after) {
				t.Fatal("missing table mutated inventory")
			}
		})
	}
}

func TestLocalUseGiftInvalidDoesNotMutate(t *testing.T) {
	c := loadGiftTestConfig()
	for _, tc := range []struct {
		name            string
		id, num, choose uint32
		setup           func(*Player) func()
	}{
		{name: "zero", id: 6020010, num: 0},
		{name: "choice", id: 6020010, num: 1, choose: 190},
		{name: "unknown", id: 4294967295, num: 1},
		{name: "not gift", id: 101, num: 1},
		{name: "insufficient", id: 6020010, num: 6},
		{name: "overflow", id: 6020010, num: 1, setup: func(p *Player) func() {
			p.Item.ItemBaseInfo[101] = &ItemBaseInfo{ItemId: 101, Num: math.MaxInt64, ItemType: proto.EBagItemTag_EBagItemTag_Currency}
			return func() {}
		}},
		{name: "random", id: 6020010, num: 1, setup: func(*Player) func() {
			r := c.Excel.Reward.PoolItems[6020010].RewardItemPoolGroup[0]
			old := r.ItemMaxCount
			r.ItemMaxCount++
			return func() { r.ItemMaxCount = old }
		}},
		{name: "missing later reward", id: 6020010, num: 1, setup: func(*Player) func() {
			r := c.Excel.Reward.PoolItems[6020012]
			delete(c.Excel.Reward.PoolItems, 6020012)
			return func() { c.Excel.Reward.PoolItems[6020012] = r }
		}},
		{name: "missing item", id: 6020010, num: 1, setup: func(*Player) func() {
			r := c.Excel.Item.ItemMap[1501]
			delete(c.Excel.Item.ItemMap, 1501)
			return func() { c.Excel.Item.ItemMap[1501] = r }
		}},
		{name: "special", id: 6020010, num: 1, setup: func(*Player) func() {
			r := c.Excel.GiftBag.Items[6020010]
			old := r.NewSpecialGiftBag
			r.NewSpecialGiftBag = 1
			return func() { r.NewSpecialGiftBag = old }
		}},
		{name: "additional cost", id: 6020010, num: 1, setup: func(*Player) func() {
			r := c.Excel.GiftBag.Items[6020010]
			old := r.UseItemID
			r.UseItemID = 101
			return func() { r.UseItemID = old }
		}},
		{name: "equipment reward", id: 6020010, num: 1, setup: func(*Player) func() {
			r := c.Excel.Item.ItemMap[190]
			old := r.NewBagItemTag
			r.NewBagItemTag = int32(proto.EBagItemTag_EBagItemTag_Weapon)
			return func() { r.NewBagItemTag = old }
		}},
	} {
		t.Run(tc.name, func(t *testing.T) {
			p := giftPlayer(6020010, 5)
			if tc.setup != nil {
				defer tc.setup(p)()
			}
			before, _ := json.Marshal(p.Item)
			rewards, pack, err := p.UseGift(tc.id, tc.num, tc.choose)
			if err == nil || rewards != nil || pack != nil {
				t.Fatalf("invalid use accepted: %v", err)
			}
			after, _ := json.Marshal(p.Item)
			if string(before) != string(after) {
				t.Fatal("failed use mutated inventory")
			}
		})
	}
}
