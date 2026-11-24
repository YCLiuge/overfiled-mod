package game

import (
	"github.com/gookit/slog"
	"gucooing/lolo/game/model"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/protocol/cmd"
	"gucooing/lolo/protocol/proto"
	"testing"
)

func TestLocalUnsupportedRouteRepliesAfterSessionCheck(t *testing.T) {
	old := log.Game
	log.Game = slog.NewStdLogger()
	defer func() { log.Game = old }()
	for _, tc := range []struct {
		name, uuid     string
		online, frozen bool
		id             uint32
		want           int
	}{
		{"missing daily task", "test", true, false, cmd.DailyTaskExchangeReq, 1},
		{"stale session", "old", true, false, cmd.DailyTaskExchangeReq, 0},
		{"offline", "test", false, false, cmd.DailyTaskExchangeReq, 0},
		{"frozen", "test", true, true, cmd.DailyTaskExchangeReq, 0},
		{"notification", "test", true, false, cmd.PlayerSceneSyncDataNotice, 0},
		{"unknown message", "test", true, false, 999999, 0},
	} {
		t.Run(tc.name, func(t *testing.T) {
			c := new(captureConn)
			p := &model.Player{UserId: 42, Conn: c, Online: tc.online, NetFreeze: tc.frozen, LoginUUID: "test"}
			g := &Game{userMap: map[uint32]*model.Player{42: p}}
			g.newRouter()
			g.routeHandle(c, 42, tc.uuid, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 123, MsgId: tc.id}})
			if len(c.bodies) != tc.want {
				t.Fatalf("reply count: %d want %d", len(c.bodies), tc.want)
			}
			if tc.want == 1 {
				rsp, ok := c.bodies[0].(*proto.DailyTaskExchangeRsp)
				if !ok || rsp.Status != proto.StatusCode_StatusCode_BAD_REQ || c.packets[0] != 123 {
					t.Fatalf("wrong failure response: %v", c.bodies)
				}
			}
			if p.Item != nil || p.Character != nil {
				t.Fatal("unsupported request changed player data")
			}
		})
	}
}
