package game

import (
	"github.com/gookit/slog"
	"gucooing/lolo/config"
	"gucooing/lolo/game/model"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/cmd"
	"gucooing/lolo/protocol/proto"
	"testing"
)

func TestLocalItemUseRouteRepliesAndUpdatesInventory(t *testing.T) {
	oldConf, oldLog := config.CONF, log.App
	defer func() { config.CONF = oldConf; log.App = oldLog }()
	config.CONF = &config.Config{Resources: &config.Resources{ResourcePath: "../Resource", DataPath: "../data"}}
	log.App = slog.NewStdLogger()
	gdconf.LoadGameConfig()
	for _, tc := range []struct {
		name   string
		req    *proto.ItemUseReq
		stock  int64
		status proto.StatusCode
	}{
		{"daily gift", &proto.ItemUseReq{ItemId: 6020010, Num: 1}, 2, proto.StatusCode_StatusCode_OK},
		{"missing stock", &proto.ItemUseReq{ItemId: 6020010, Num: 1}, 0, proto.StatusCode_StatusCode_ITEM_NOT_ENOUGH},
		{"zero quantity", &proto.ItemUseReq{ItemId: 6020010}, 2, proto.StatusCode_StatusCode_BAD_REQ},
		{"unsupported selection", &proto.ItemUseReq{ItemId: 6020010, Num: 1, ChooseItemId: 101}, 2, proto.StatusCode_StatusCode_BAD_REQ},
		{"nil request", nil, 2, proto.StatusCode_StatusCode_BAD_REQ},
	} {
		t.Run(tc.name, func(t *testing.T) {
			config.GetGame().Sandbox = false
			c := new(captureConn)
			p := &model.Player{UserId: 42, Conn: c, Online: true, LoginUUID: "test"}
			p.GetItemModel().AddItemBase(6020010, tc.stock)
			g := &Game{userMap: map[uint32]*model.Player{42: p}}
			g.newRouter()
			g.routeHandle(c, 42, "test", &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 75, MsgId: cmd.ItemUseReq}, Body: tc.req})
			replies, notices := 0, 0
			for j, body := range c.bodies {
				switch r := body.(type) {
				case *proto.ItemUseRsp:
					replies++
					if c.packets[j] != 75 || r.Status != tc.status {
						t.Fatalf("response: %v packet=%d", r, c.packets[j])
					}
					if tc.status == proto.StatusCode_StatusCode_OK && len(r.Items) != 3 {
						t.Fatalf("missing rewards: %v", r)
					}
				case *proto.PackNotice:
					notices++
					if c.packets[j] != 0 || len(r.Items) != 4 {
						t.Fatalf("bad inventory update: %v", r)
					}
				}
			}
			if replies != 1 {
				t.Fatalf("reply count: %d", replies)
			}
			wantStock := tc.stock
			if tc.status == proto.StatusCode_StatusCode_OK {
				wantStock--
				if notices != 1 {
					t.Fatal("missing pack notice")
				}
			} else if notices != 0 {
				t.Fatal("failed request sent inventory changes")
			}
			if got := p.GetItemModel().GetItemBaseInfo(6020010).Num; got != wantStock {
				t.Fatalf("stock got %d want %d", got, wantStock)
			}
		})
	}
	t.Run("sandbox via config", func(t *testing.T) {
		config.GetGame().Sandbox = true
		c := new(captureConn)
		p := &model.Player{Conn: c}
		g := new(Game)
		g.ItemUse(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 76}, Body: &proto.ItemUseReq{ItemId: 6020010, Num: 1}})
		if len(c.bodies) != 2 {
			t.Fatalf("sandbox reply count: %d", len(c.bodies))
		}
		rsp, ok := c.bodies[1].(*proto.ItemUseRsp)
		if !ok || rsp.Status != proto.StatusCode_StatusCode_OK || c.packets[1] != 76 {
			t.Fatalf("sandbox response: %v", c.bodies)
		}
		if p.Item.GetItemBaseInfo(6020010) != nil || p.Item.GetItemBaseInfo(101).Num != 1000 {
			t.Fatal("sandbox did not bypass gift stock correctly")
		}
	})
}
