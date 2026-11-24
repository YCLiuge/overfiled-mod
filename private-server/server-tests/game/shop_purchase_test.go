package game

import (
	"github.com/gookit/slog"
	pb "google.golang.org/protobuf/proto"
	"gucooing/lolo/config"
	"gucooing/lolo/game/model"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/cmd"
	"gucooing/lolo/protocol/proto"
	"testing"
)

func TestLocalShopPurchaseRoutes(t *testing.T) {
	oldConf, oldApp, oldGame := config.CONF, log.App, log.Game
	defer func() { config.CONF = oldConf; log.App = oldApp; log.Game = oldGame }()
	config.CONF = &config.Config{Resources: &config.Resources{ResourcePath: "../Resource", DataPath: "../data"}}
	config.GetGame().Sandbox = true
	log.App = slog.NewStdLogger()
	log.Game = slog.NewStdLogger()
	gdconf.LoadGameConfig()
	c := new(captureConn)
	p := &model.Player{UserId: 42, Online: true, LoginUUID: "test", Conn: c}
	g := &Game{userMap: map[uint32]*model.Player{42: p}}
	g.newRouter()
	route := func(id uint32, body pb.Message) {
		c.bodies = nil
		c.packets = nil
		g.routeHandle(c, 42, "test", &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 71, MsgId: id}, Body: body})
	}
	route(cmd.ShopInfoReq, &proto.ShopInfoReq{ShopId: 100})
	info := c.bodies[0].(*proto.ShopInfoRsp)
	if info.Status != proto.StatusCode_StatusCode_OK || len(info.Grids) != 6 || info.Grids[0].PoolId != 100 || info.Grids[5].PoolIndex != 6 {
		t.Fatalf("bad shop listing: %v", info)
	}
	// Both paid-currency purchase and free source gift work without any balance.
	route(cmd.ShopBuyReq, &proto.ShopBuyReq{ShopId: 400, GridId: 2, ItemId: 6020020, BuyTimes: 3})
	if len(c.bodies) != 2 {
		t.Fatalf("missing inventory/response: %v", c.bodies)
	}
	pack := c.bodies[0].(*proto.PackNotice)
	buy := c.bodies[1].(*proto.ShopBuyRsp)
	if c.packets[0] != 0 || c.packets[1] != 71 || buy.Status != proto.StatusCode_StatusCode_OK || (buy.Grids.BuyTimes != 0 || p.Shop.ShopInfos[400].ShopGrids[2].BuyTimes != 3) || len(pack.Items) != 1 || pack.Items[0].GetMainItem().GetBaseItem().GetNum() != 3 {
		t.Fatalf("incomplete shop purchase: %v", c.bodies)
	}
	for attempt, pool := range []uint32{100, 150} {
		route(cmd.CreatePayOrderReq, &proto.CreatePayOrderReq{ShopId: 100, GridId: 1, PoolId: pool})
		if len(c.bodies) != 3 {
			t.Fatalf("checkout missing replies: %v", c.bodies)
		}
		rsp := c.bodies[0].(*proto.CreatePayOrderRsp)
		inv := c.bodies[1].(*proto.PackNotice)
		goods := c.bodies[2].(*proto.PaySendGoodsNotice)
		delta := int64(120)
		balance := int64(120)
		if attempt == 1 {
			delta = 60
			balance = 180
		}
		if rsp.Status != proto.StatusCode_StatusCode_OK || rsp.OrderId == "" || goods.OrderId != rsp.OrderId || goods.Grids.PoolId != 150 || goods.Grids.BuyTimes != uint32(attempt+1) || goods.Items[0].GetMainItem().GetBaseItem().GetNum() != delta || inv.Items[0].GetMainItem().GetBaseItem().GetNum() != balance || c.packets[0] != 71 || c.packets[1] != 0 || c.packets[2] != 0 {
			t.Fatalf("bad checkout completion: %v", c.bodies)
		}
	}
	before := p.Item.ItemBaseInfo[108].Num
	for _, req := range []*proto.CreatePayOrderReq{{ShopId: 100, GridId: 1, PoolId: 100}, {ShopId: 100, GridId: 1, ToPlayerId: 999}, {ShopId: 200, GridId: 1}} {
		route(cmd.CreatePayOrderReq, req)
		if len(c.bodies) != 1 || c.bodies[0].(*proto.CreatePayOrderRsp).Status != proto.StatusCode_StatusCode_BAD_REQ || p.Item.ItemBaseInfo[108].Num != before {
			t.Fatal("invalid payment mutated state or sent fake success")
		}
	}
	character := gdconf.GetCharacterInfoByShop(1101001)
	if character == nil {
		t.Fatal("missing real character shop mapping")
	}
	characterID := uint32(character.ID)
	p.Character = &model.CharacterModel{CharacterMap: map[uint32]*model.CharacterInfo{characterID: {CharacterId: characterID}}}
	route(cmd.ShopBuyReq, &proto.ShopBuyReq{ShopId: 1101001, GridId: 1, BuyTimes: 1})
	foundBuy, foundUnlock := false, false
	for i, body := range c.bodies {
		switch r := body.(type) {
		case *proto.ShopBuyRsp:
			foundBuy = r.Status == proto.StatusCode_StatusCode_OK && len(r.Items) == 0 && c.packets[i] == 71
		case *proto.CharacterBpBuyNotice:
			foundUnlock = r.CharacterId == characterID && c.packets[i] == 0
		}
	}
	if !foundBuy || !foundUnlock || !p.Character.CharacterMap[characterID].IsUnlockPayment {
		t.Fatalf("missing character entitlement completion: %v", c.bodies)
	}
	config.GetGame().Sandbox = false
	route(cmd.CreatePayOrderReq, &proto.CreatePayOrderReq{ShopId: 100, GridId: 1})
	if len(c.bodies) != 1 || c.bodies[0].(*proto.CreatePayOrderRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("strict mode cannot fake payment")
	}
	route(cmd.ShopBuyReq, &proto.ShopBuyReq{ShopId: 400, GridId: 999, ItemId: 6020020, BuyTimes: 1})
	if len(c.bodies) != 1 || c.bodies[0].(*proto.ShopBuyRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("unknown grid must reply once without panic")
	}
}
