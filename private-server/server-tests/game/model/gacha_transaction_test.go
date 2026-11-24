package model

import (
	"encoding/json"
	"fmt"
	"math"
	"testing"

	"github.com/gookit/slog"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/proto"
)

func gachaSnapshot(p *Player) string { b, _ := json.Marshal(p); return string(b) }
func TestLocalGachaEveryReward(t *testing.T) {
	loadGiftTestConfig()
	old := log.Game
	log.Game = slog.NewStdLogger()
	defer func() { log.Game = old }()
	seen := map[uint32]bool{}
	for _, pool := range gdconf.GetSandboxGachas() {
		for _, rewards := range pool.Pools {
			for _, v := range rewards.Items {
				id := uint32(v.ItemID)
				if seen[id] {
					continue
				}
				seen[id] = true
				t.Run(fmt.Sprint(id), func(t *testing.T) {
					p := &Player{}
					ctx, err := p.NewGachaCtxMode(&proto.GachaReq{GachaId: 2000}, true)
					if err != nil {
						t.Fatal(err)
					}
					if _, err = ctx.grant(id); err != nil {
						t.Fatal(err)
					}
					if _, err = ctx.grant(id); err != nil {
						t.Fatal(err)
					}
				})
			}
		}
	}
	t.Logf("validated real reward items=%d", len(seen))
}

func TestLocalGachaEveryPool(t *testing.T) {
	loadGiftTestConfig()
	old := log.Game
	log.Game = slog.NewStdLogger()
	defer func() { log.Game = old }()
	pools := gdconf.GetSandboxGachas()
	success := 0
	for _, pool := range pools {
		t.Run(pool.Conf.String(), func(t *testing.T) {
			p := &Player{UserId: 42}
			p.ApplySandbox()
			id := uint32(pool.Conf.ID)
			if pool.IsOptional() {
				if err := p.ChooseGachaPool(id, uint32(pool.Conf.CardPoolRange[0])); err != nil {
					t.Fatal(err)
				}
			}
			for run := 0; run < 2; run++ {
				ctx, err := p.NewGachaCtxMode(&proto.GachaReq{GachaId: id}, true)
				if err != nil {
					t.Fatal(err)
				}
				before := gachaSnapshot(p)
				if ctx.staged == p || before != gachaSnapshot(p) {
					t.Fatal("prepare changed player")
				}
				if err = ctx.Run(); err != nil {
					t.Fatal(err)
				}
				if len(ctx.ItemDetails) != int(pool.Conf.Method2Count) || ctx.ItemTx == nil || len(ctx.ItemTx.PackNotice.Items) == 0 {
					t.Fatal("missing reward or notice")
				}
				if p.Gacha.GachaMap[id].GachaTimes != uint32(run+1)*uint32(pool.Conf.Method2Count) {
					t.Fatal("counter")
				}
				for _, r := range p.Gacha.Records {
					if r == nil || r.ItemId == 0 {
						t.Fatal("bad history")
					}
				}
				if ctx.Run() == nil {
					t.Fatal("context replay allowed")
				}
			}
			encoded := gachaSnapshot(p)
			var restored Player
			if err := json.Unmarshal([]byte(encoded), &restored); err != nil {
				t.Fatal(err)
			}
			records, pages, err := restored.SandboxGachaRecords(id, 1)
			if err != nil || len(records) != 5 || pages != 4 {
				t.Fatalf("history %d %d %v", len(records), pages, err)
			}
			success++
		})
	}
	t.Logf("open pools=%d successful pools=%d", len(pools), success)
}
func TestLocalGachaSelectionAndUnlimited(t *testing.T) {
	loadGiftTestConfig()
	old := log.Game
	log.Game = slog.NewStdLogger()
	defer func() { log.Game = old }()
	p := &Player{}
	optional := gdconf.GetGachaRewardPool(gdconf.GetGachaData(2000).Conf.OptionalPoolID)
	selected := uint32(optional.Items[0].ItemID)
	if err := p.ChooseGachaItem(2000, selected); err != nil {
		t.Fatal(err)
	}
	p.Gacha.GetGachaInfo(2000).GachaTimes = 299
	ctx, err := p.NewGachaCtxMode(&proto.GachaReq{GachaId: 2000, IsSingle: true}, true)
	if err != nil {
		t.Fatal(err)
	}
	if err = ctx.Run(); err != nil {
		t.Fatal(err)
	}
	if p.Gacha.Records[0].ItemId != selected {
		t.Fatal("optional threshold ignored")
	}
	fashion := gdconf.GetGachaData(4000)
	var wish uint32
	for _, pool := range fashion.Pools {
		for _, v := range pool.Items {
			if v.IsOptional {
				wish = uint32(v.ItemID)
			}
		}
	}
	if wish == 0 {
		t.Fatal("missing real wish")
	}
	if err = p.ChooseGachaItem(4000, wish); err != nil {
		t.Fatal(err)
	}
	p.Gacha.GetGachaTypeInfo(proto.EUIGachaType(fashion.Conf.NewUIGachaType)).GuaranteeDesire = 69
	ctx, err = p.NewGachaCtxMode(&proto.GachaReq{GachaId: 4000}, true)
	if err != nil {
		t.Fatal(err)
	}
	if err = ctx.Run(); err != nil {
		t.Fatal(err)
	}
	if p.Gacha.Records[1].ItemId != wish {
		t.Fatal("wish guarantee ignored")
	}
	for i := 0; i < 6; i++ {
		ctx, err = p.NewGachaCtxMode(&proto.GachaReq{GachaId: 1000}, true)
		if err != nil {
			t.Fatal(err)
		}
		if err = ctx.Run(); err != nil {
			t.Fatal(err)
		}
	}
	if p.Gacha.GachaMap[1000].GachaTimes != 60 {
		t.Fatal("sandbox limit")
	}
}

func TestLocalGachaBoundariesAndRollback(t *testing.T) {
	loadGiftTestConfig()
	old := log.Game
	log.Game = slog.NewStdLogger()
	defer func() { log.Game = old }()
	p := &Player{}
	for _, req := range []*proto.GachaReq{nil, {GachaId: 0}, {GachaId: math.MaxUint32}, {GachaId: 1000, IsSingle: true}, {GachaId: 4000, IsSingle: true}, {GachaId: 9000}} {
		before := gachaSnapshot(p)
		if _, err := p.NewGachaCtxMode(req, true); err == nil {
			t.Fatalf("accepted %v", req)
		}
		if before != gachaSnapshot(p) {
			t.Fatal("failed prepare mutated player")
		}
	}
	before := gachaSnapshot(p)
	if p.ChooseGachaPool(9000, 2000) == nil || p.ChooseGachaItem(4000, 999999) == nil {
		t.Fatal("accepted invalid selection")
	}
	if before != gachaSnapshot(p) {
		t.Fatal("selection changed player")
	}
	if _, err := p.NewGachaCtxMode(&proto.GachaReq{GachaId: 2000}, false); err != ErrGachaStrict {
		t.Fatal(err)
	}
	// Any overflow encountered after drawing must leave the entire live player intact.
	p.Item = &ItemModel{ItemBaseInfo: map[uint32]*ItemBaseInfo{401: {ItemId: 401, Num: math.MaxInt64, ItemType: proto.EBagItemTag_EBagItemTag_DyeStuff}}}
	before = gachaSnapshot(p)
	ctx, err := p.NewGachaCtxMode(&proto.GachaReq{GachaId: 2000}, true)
	if err != nil {
		t.Fatal(err)
	}
	// Force each sampled category to an overflowing real stackable reward.
	for key := range ctx.pools {
		ctx.pools[key] = []int32{401}
	}
	if err = ctx.Run(); err == nil {
		t.Fatal("expected overflow")
	}
	if before != gachaSnapshot(p) {
		t.Fatal("partial grant escaped")
	}
}
func TestLocalGachaAllSelectorsAndRepeat(t *testing.T) {
	loadGiftTestConfig()
	old := log.Game
	log.Game = slog.NewStdLogger()
	defer func() { log.Game = old }()
	for _, pool := range gdconf.GetSandboxGachas() {
		if !pool.IsOptional() {
			continue
		}
		for _, child := range pool.Conf.CardPoolRange {
			p := &Player{}
			if err := p.ChooseGachaPool(uint32(pool.Conf.ID), uint32(child)); err != nil {
				t.Fatal(err)
			}
			ctx, err := p.NewGachaCtxMode(&proto.GachaReq{GachaId: uint32(pool.Conf.ID)}, true)
			if err != nil {
				t.Fatal(err)
			}
			if err = ctx.Run(); err != nil {
				t.Fatal(err)
			}
			if ctx.conf.Conf.ID != child {
				t.Fatal("wrong real rewards")
			}
		}
	}
	p := &Player{}
	p.ApplySandbox()
	ctx, err := p.NewGachaCtxMode(&proto.GachaReq{GachaId: 2000}, true)
	if err != nil {
		t.Fatal(err)
	}
	// Force an owned character draw, verifying configured repeat token, not a new character.
	id := uint32(ctx.pools[1][0])
	conf := gdconf.GetCharacterAll(id)
	if conf == nil {
		t.Fatal(id)
	}
	n := len(ctx.staged.Character.CharacterMap)
	d, err := ctx.grant(id)
	if err != nil {
		t.Fatal(err)
	}
	if d.MainItem.ItemId != uint32(conf.CharacterInfo.RepeatItemID) || len(ctx.staged.Character.CharacterMap) != n {
		t.Fatal("duplicate conversion")
	}
	fashion, err := p.NewGachaCtxMode(&proto.GachaReq{GachaId: 4000}, true)
	if err != nil {
		t.Fatal(err)
	}
	fid := uint32(fashion.pools[5][0])
	if _, err = fashion.grant(fid); err != nil {
		t.Fatal(err)
	}
	n = len(fashion.staged.Item.ItemFashionMap)
	if d, err = fashion.grant(fid); err != nil || d.MainItem.ItemId != fid || len(fashion.staged.Item.ItemFashionMap) != n {
		t.Fatal("fashion duplicate")
	}
}
