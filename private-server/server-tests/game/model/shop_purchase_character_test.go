package model

import (
	"errors"
	"testing"

	"gucooing/lolo/gdconf"
)

func TestLocalPurchaseShopCharacterEntitlement(t *testing.T) {
	loadGiftTestConfig()
	conf := gdconf.GetCharacterInfoByShop(1101001)
	if conf == nil {
		t.Fatal("missing actual character shop mapping")
	}
	newPlayer := func(balance int64) *Player {
		p := shopCurrencyPlayer(balance)
		p.Character = &CharacterModel{CharacterMap: map[uint32]*CharacterInfo{uint32(conf.ID): {CharacterId: uint32(conf.ID)}}}
		return p
	}
	p := newPlayer(1000)
	rewards, notice, grid, err := p.PurchaseShop(1101001, 1, 0, 1, false)
	if err != nil || len(rewards) != 0 || giftCounts(notice.Items)[108] != 500 || grid.BuyTimes != 1 || !p.Character.CharacterMap[uint32(conf.ID)].IsUnlockPayment {
		t.Fatal(rewards, notice, grid, err)
	}
	before := shopSnapshot(p)
	if _, _, _, err = p.PurchaseShop(1101001, 1, 0, 1, false); !errors.Is(err, ErrShopLimit) {
		t.Fatal(err)
	}
	if shopSnapshot(p) != before {
		t.Fatal("repeat unlock charged again")
	}
	p = newPlayer(0)
	for run := uint32(1); run <= 2; run++ {
		r, n, g, e := p.PurchaseShop(1101001, 1, 0, 3, true)
		if e != nil || len(r) != 0 || len(n.Items) != 0 || g.BuyTimes != run*3 || !p.Character.CharacterMap[uint32(conf.ID)].IsUnlockPayment || p.Item.ItemBaseInfo[108].Num != 0 {
			t.Fatal(r, n, g, e)
		}
	}
	p = newPlayer(499)
	before = shopSnapshot(p)
	if _, _, _, err = p.PurchaseShop(1101001, 1, 0, 1, false); !errors.Is(err, ErrShopNotEnough) {
		t.Fatal(err)
	}
	if shopSnapshot(p) != before || p.Character.CharacterMap[uint32(conf.ID)].IsUnlockPayment {
		t.Fatal("failed purchase unlocked or spent")
	}
	p = &Player{}
	if _, _, _, err = p.PurchaseShop(1101001, 1, 0, 1, true); !errors.Is(err, ErrShopConfig) || p.Character != nil || p.Shop != nil || p.Item != nil {
		t.Fatal("missing character initialized state", err)
	}
}
