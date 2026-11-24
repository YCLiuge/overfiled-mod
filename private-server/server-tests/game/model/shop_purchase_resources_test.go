package model

import (
	"errors"
	"testing"

	"gucooing/lolo/gdconf"
	"gucooing/lolo/protocol/proto"
)

// Exercise the actual packaged catalog rather than assuming that the few daily
// gift fixtures represent all ordinary inventory products.
func TestLocalPurchaseShopRealCatalogBaseProducts(t *testing.T) {
	cfg := loadGiftTestConfig()
	counts := map[proto.EBagItemTag]int{}
	for shopID := range cfg.Excel.Shop.Info {
		for gridID := range gdconf.GetGridsByShopId(shopID) {
			p := &Player{}
			offer, _, err := p.ResolveShopOffer(shopID, gridID, 0)
			if err != nil {
				continue
			}
			item := gdconf.GetItemConfigure(uint32(offer.ItemID))
			if item == nil || !shopBaseTag(proto.EBagItemTag(item.NewBagItemTag)) {
				continue
			}
			rewards, notice, grid, err := p.PurchaseShop(shopID, gridID, uint32(offer.ItemID), 2, true)
			// Explicit special-shop semantics and invalid resource references remain
			// safely rejected; supported fixed base products must really arrive.
			if errors.Is(err, ErrShopUnsupported) || errors.Is(err, ErrShopConfig) {
				continue
			}
			if err != nil {
				t.Fatalf("shop %d grid %d item %d: %v", shopID, gridID, offer.ItemID, err)
			}
			if len(rewards) == 0 || len(notice.Items) == 0 || grid.BuyTimes != 2 || p.Item.ItemBaseInfo[uint32(offer.ItemID)].Num < int64(offer.ItemNum) {
				t.Fatalf("missing product shop %d grid %d", shopID, gridID)
			}
			counts[proto.EBagItemTag(item.NewBagItemTag)]++
		}
	}
	for _, tag := range []proto.EBagItemTag{proto.EBagItemTag_EBagItemTag_Currency, proto.EBagItemTag_EBagItemTag_Gift, proto.EBagItemTag_EBagItemTag_Material, proto.EBagItemTag_EBagItemTag_Furniture, proto.EBagItemTag_EBagItemTag_DyeStuff, proto.EBagItemTag_EBagItemTag_Catcher} {
		if counts[tag] == 0 {
			t.Fatalf("no real resource products exercised for %s", tag)
		}
	}
	t.Logf("successful real catalog purchases by tag: %v", counts)
}
