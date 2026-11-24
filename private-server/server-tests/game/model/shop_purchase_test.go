package model

import (
	"encoding/json"
	"errors"
	"math"
	"reflect"
	"testing"

	"gucooing/lolo/gdconf"
	"gucooing/lolo/protocol/excel"
	"gucooing/lolo/protocol/proto"
)

func shopCurrencyPlayer(num int64) *Player {
	return &Player{Item: &ItemModel{ItemBaseInfo: map[uint32]*ItemBaseInfo{108: {ItemId: 108, Num: num, ItemType: proto.EBagItemTag_EBagItemTag_Currency, PackType: proto.ItemDetail_PackType_Inventory}}}}
}
func shopSnapshot(p *Player) string {
	b, _ := json.Marshal(struct {
		Shop *ShopModel
		Item *ItemModel
	}{p.Shop, p.Item})
	return string(b)
}
func TestLocalPurchaseShopSandboxAndPersistence(t *testing.T) {
	loadGiftTestConfig()
	for _, p := range []*Player{{}, {Item: &ItemModel{}}, shopCurrencyPlayer(0), shopCurrencyPlayer(123)} {
		beforeBalance := int64(0)
		if p.Item != nil && p.Item.ItemBaseInfo[108] != nil {
			beforeBalance = p.Item.ItemBaseInfo[108].Num
		}
		for run := uint32(1); run <= 2; run++ {
			rewards, notice, grid, err := p.PurchaseShop(400, 2, 6020020, 3, true)
			if err != nil {
				t.Fatal(err)
			}
			if got := giftCounts(rewards); !reflect.DeepEqual(got, map[uint32]int64{6020020: 3}) {
				t.Fatal(got)
			}
			if len(notice.Items) != 1 || notice.Items[0].MainItem.GetBaseItem().Num != int64(run*3) {
				t.Fatal(notice)
			}
			if grid.BuyTimes != run*3 || grid.PoolId != 402 || grid.PoolIndex != 1 {
				t.Fatal(grid)
			}
		}
		if balance := p.Item.ItemBaseInfo[108]; balance != nil && balance.Num != beforeBalance {
			t.Fatal("sandbox spent currency")
		}
		data, err := json.Marshal(p)
		if err != nil {
			t.Fatal(err)
		}
		var restored Player
		if err = json.Unmarshal(data, &restored); err != nil {
			t.Fatal(err)
		}
		if restored.Shop.ShopInfos[400].ShopGrids[2].BuyTimes != 6 || restored.Shop.ShopInfos[400].ShopGrids[2].PoolIndex != 1 || restored.Item.ItemBaseInfo[6020020].Num != 6 {
			t.Fatal("lost persistent purchase")
		}
	}
}
func TestLocalPurchaseShopStrictCurrencyAndLimit(t *testing.T) {
	loadGiftTestConfig()
	p := shopCurrencyPlayer(100)
	rewards, notice, grid, err := p.PurchaseShop(400, 2, 6020020, 1, false)
	if err != nil {
		t.Fatal(err)
	}
	if !reflect.DeepEqual(giftCounts(rewards), map[uint32]int64{6020020: 1}) || !reflect.DeepEqual(giftCounts(notice.Items), map[uint32]int64{108: 80, 6020020: 1}) || grid.BuyTimes != 1 {
		t.Fatal(rewards, notice, grid)
	}
	before := shopSnapshot(p)
	if _, _, _, err = p.PurchaseShop(400, 2, 6020020, 1, false); !errors.Is(err, ErrShopLimit) {
		t.Fatal(err)
	}
	if shopSnapshot(p) != before {
		t.Fatal("limit failure changed state")
	}
}
func TestLocalPurchaseShopFailuresLeaveStateUntouched(t *testing.T) {
	loadGiftTestConfig()
	for _, p := range []*Player{{}, {Item: &ItemModel{}}, shopCurrencyPlayer(19)} {
		cases := []struct {
			shop, grid, item, count uint32
			sandbox                 bool
			want                    error
		}{
			{400, 2, 6020020, 0, true, ErrShopInvalid},
			{999999, 2, 6020020, 1, true, ErrShopInvalid},
			{400, 99999, 6020020, 1, true, ErrShopInvalid},
			{400, 2, 6020010, 1, true, ErrShopInvalid},
			{400, 2, 6020020, 1, false, ErrShopNotEnough},
			{100, 1, 108, 1, false, ErrShopUnsupported},
		}
		for _, tc := range cases {
			before := shopSnapshot(p)
			r, n, g, err := p.PurchaseShop(tc.shop, tc.grid, tc.item, tc.count, tc.sandbox)
			if !errors.Is(err, tc.want) || r != nil || n != nil || g != nil {
				t.Fatalf("%+v: %v", tc, err)
			}
			if shopSnapshot(p) != before {
				t.Fatal("failure initialized or changed state")
			}
		}
	}
}
func TestLocalPurchaseShopFirstPoolAndBatch(t *testing.T) {
	loadGiftTestConfig()
	p := &Player{}
	offer, pool, err := p.ResolveShopOffer(100, 1, 0)
	if err != nil || pool != 100 || offer.ItemID != 108 || p.Shop != nil || p.Item != nil {
		t.Fatal(offer, pool, err)
	}
	for run, want := range []int64{120, 60} {
		r, _, g, e := p.PurchaseShop(100, 1, 108, 1, true)
		if e != nil || giftCounts(r)[108] != want || g.BuyTimes != uint32(run+1) || g.PoolId != 150 {
			t.Fatal(r, g, e)
		}
	}
	if _, pool, err = p.ResolveShopOffer(100, 1, 108); err != nil || pool != 150 {
		t.Fatal(pool, err)
	}
	batch := &Player{}
	r, _, _, err := batch.PurchaseShop(100, 1, 108, 3, true)
	if err != nil || giftCounts(r)[108] != 240 {
		t.Fatal(r, err)
	}
}
func TestLocalPurchaseShopOverflowAtomicity(t *testing.T) {
	loadGiftTestConfig()
	p := shopCurrencyPlayer(100)
	p.Item.ItemBaseInfo[6020020] = &ItemBaseInfo{ItemId: 6020020, Num: math.MaxInt64, ItemType: proto.EBagItemTag_EBagItemTag_Gift, PackType: proto.ItemDetail_PackType_Inventory}
	before := shopSnapshot(p)
	if _, _, _, err := p.PurchaseShop(400, 2, 6020020, 1, false); !errors.Is(err, ErrShopOverflow) {
		t.Fatal(err)
	}
	if shopSnapshot(p) != before {
		t.Fatal("overflow deducted price")
	}
	p = &Player{Shop: &ShopModel{ShopInfos: map[uint32]*ShopInfo{400: {ShopGrids: map[uint32]*GridInfo{2: {BuyTimes: math.MaxUint32}}}}}}
	before = shopSnapshot(p)
	if _, _, _, err := p.PurchaseShop(400, 2, 6020020, 1, true); !errors.Is(err, ErrShopOverflow) {
		t.Fatal(err)
	}
	if shopSnapshot(p) != before {
		t.Fatal("counter overflow changed state")
	}
}
func TestLocalPurchaseShopMultiplePricesAndUnsupported(t *testing.T) {
	loadGiftTestConfig()
	offer := gdconf.GetPools(402)[1]
	oldPrices := offer.ShopCurrencyItem
	defer func() { offer.ShopCurrencyItem = oldPrices }()
	offer.ShopCurrencyItem = []*excel.ShopCurrencyItems{{CurrencyID: 108, Price: 15}, {CurrencyID: 108, Price: 15}, {CurrencyID: 101, Price: 1}}
	p := shopCurrencyPlayer(100)
	before := shopSnapshot(p)
	if _, _, _, err := p.PurchaseShop(400, 2, 6020020, 1, false); !errors.Is(err, ErrShopNotEnough) {
		t.Fatal(err)
	}
	if shopSnapshot(p) != before {
		t.Fatal("partial multi-price deduction")
	}
	p.Item.AddItemBase(101, 1)
	_, notice, _, err := p.PurchaseShop(400, 2, 6020020, 1, false)
	if err != nil || giftCounts(notice.Items)[108] != 70 || giftCounts(notice.Items)[101] != 0 {
		t.Fatal(notice, err)
	}
	// A later unsupported product must not consume any price.
	oldItem := offer.ItemID
	defer func() { offer.ItemID = oldItem }()
	offer.ItemID = 4200251 // real fashion resource, requires an outfit transaction
	p = shopCurrencyPlayer(100)
	before = shopSnapshot(p)
	if _, _, _, err = p.PurchaseShop(400, 2, 4200251, 1, true); !errors.Is(err, ErrShopUnsupported) {
		t.Fatal(err)
	}
	if shopSnapshot(p) != before {
		t.Fatal("unsupported item changed state")
	}
}
func TestLocalPurchaseShopInvalidStoredSlotAndNonrepeatable(t *testing.T) {
	loadGiftTestConfig()
	p := shopCurrencyPlayer(100)
	p.Item.ItemBaseInfo[108].PackType = proto.ItemDetail_PackType(99)
	before := shopSnapshot(p)
	if _, _, _, err := p.PurchaseShop(400, 2, 6020020, 1, false); !errors.Is(err, ErrShopConfig) {
		t.Fatal(err)
	}
	if shopSnapshot(p) != before {
		t.Fatal("invalid slot changed state")
	}
	offer := gdconf.GetPools(402)[1]
	old := offer.IsRepeatedInBag
	offer.IsRepeatedInBag = false
	defer func() { offer.IsRepeatedInBag = old }()
	p = shopCurrencyPlayer(100)
	p.Item.AddItemBase(6020020, 1)
	before = shopSnapshot(p)
	if _, _, _, err := p.PurchaseShop(400, 2, 6020020, 1, false); !errors.Is(err, ErrShopLimit) {
		t.Fatal(err)
	}
	if shopSnapshot(p) != before {
		t.Fatal("nonrepeatable rejection changed state")
	}
	if _, _, _, err := p.PurchaseShop(400, 2, 6020020, 2, true); err != nil {
		t.Fatal(err)
	}
}

func TestLocalPurchaseShopNonDefaultPoolIndex(t *testing.T) {
	loadGiftTestConfig()
	pools := gdconf.GetPools(402)
	old := pools[1]
	delete(pools, 1)
	pools[7] = &excel.ShopPoolConfigureItem{Index: 7, ItemID: 6020020, ItemNum: 1, MinLevel: 1, MaxLevel: 100, Discount: 1}
	defer func() { delete(pools, 7); pools[1] = old }()
	p := &Player{}
	_, _, g, err := p.PurchaseShop(400, 2, 6020020, 1, true)
	if err != nil || g.PoolIndex != 7 || p.Shop.ShopInfos[400].ShopGrids[2].PoolIndex != 7 {
		t.Fatal(g, err)
	}
}
