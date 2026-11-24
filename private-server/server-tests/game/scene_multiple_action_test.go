package game

import (
	"github.com/gookit/slog"
	pb "google.golang.org/protobuf/proto"
	"gucooing/lolo/config"
	"gucooing/lolo/game/model"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/pkg/ofnet"
	"gucooing/lolo/protocol/cmd"
	"gucooing/lolo/protocol/excel"
	"gucooing/lolo/protocol/proto"
	"testing"
	"time"
)

type actionCaptureConn struct {
	ofnet.Conn
	messages []pb.Message
	packets  []uint32
}

func (c *actionCaptureConn) Send(id uint32, msg pb.Message) {
	c.messages = append(c.messages, pb.Clone(msg))
	c.packets = append(c.packets, id)
}
func (c *actionCaptureConn) SendBin(id, command uint32, body []byte) {
	var msg pb.Message
	switch command {
	case cmd.SendActionNotice:
		msg = new(proto.SendActionNotice)
	case cmd.SendMultipleActionNotice:
		msg = new(proto.SendMultipleActionNotice)
	case cmd.SendMultipleActionCompleteNotice:
		msg = new(proto.SendMultipleActionCompleteNotice)
	default:
		panic(command)
	}
	if err := pb.Unmarshal(body, msg); err != nil {
		panic(err)
	}
	c.Send(id, msg)
}
func actionFixture(t *testing.T) (*Game, *ChannelInfo, []*ScenePlayer, []*actionCaptureConn) {
	t.Helper()
	oldConf, oldApp, oldGame := config.CONF, log.App, log.Game
	config.CONF = &config.Config{Resources: &config.Resources{ResourcePath: "../Resource", DataPath: "../data"}}
	log.App = slog.NewStdLogger()
	log.Game = log.App
	gdconf.LoadGameConfig()
	t.Cleanup(func() { config.CONF = oldConf; log.App = oldApp; log.Game = oldGame })
	g := new(Game)
	c := &ChannelInfo{allPlayer: map[uint32]*ScenePlayer{}, actionSyncChan: make(chan *ActionSyncCtx, 10), multipleAcceptChan: make(chan *multipleActionAcceptCtx, 10), doneChan: make(chan struct{})}
	var players []*ScenePlayer
	var conns []*actionCaptureConn
	for i := uint32(1); i <= 4; i++ {
		conn := new(actionCaptureConn)
		p := &model.Player{UserId: i, Conn: conn}
		sp := &ScenePlayer{Player: p, channelInfo: c}
		c.allPlayer[i] = sp
		g.getWordInfo().allScenePlayer.Store(i, sp)
		players = append(players, sp)
		conns = append(conns, conn)
	}
	return g, c, players, conns
}
func acceptAction(c *ChannelInfo, p *ScenePlayer, from, action uint32, now int64) {
	c.acceptMultipleAction(&multipleActionAcceptCtx{player: p, packetID: 77, req: &proto.MultipleActionAcceptReq{FromPlayerId: from, ActionId: action}}, now)
}
func lastAccept(t *testing.T, c *actionCaptureConn) proto.StatusCode {
	t.Helper()
	for i := len(c.messages) - 1; i >= 0; i-- {
		if r, ok := c.messages[i].(*proto.MultipleActionAcceptRsp); ok {
			return r.Status
		}
	}
	t.Fatal("missing accept response")
	return 0
}
func TestMultipleActionInviteAcceptRoomSync(t *testing.T) {
	g, c, p, conns := actionFixture(t)
	g.newRouter()
	if g.handlerFuncRouteMap[cmd.MultipleActionAcceptReq] == nil {
		t.Fatal("missing accept route")
	}
	cfg := gdconf.GetAppearance().MultipleActions[10000]
	if cfg == nil || cfg.StarterPlayActionID != 10001 || cfg.RequesterPlayActionID != 10002 || cfg.PlayerNumber != 2 {
		t.Fatal("resource role mapping lost")
	}
	g.SendAction(p[0].Player, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 12}, Body: &proto.SendActionReq{ActionId: 10000}})
	c.SendActionNotice(<-c.actionSyncChan)
	now := time.Now().Unix()
	inv := c.multipleActions[1]
	if inv == nil || inv.endTime < now+29 || inv.endTime > now+31 {
		t.Fatal("wrong Unix-second deadline")
	}
	for _, conn := range conns {
		var invite bool
		for _, m := range conn.messages {
			if n, ok := m.(*proto.SendActionNotice); ok {
				if n.MultipleNeedCount != 1 || n.EndTime != inv.endTime {
					t.Fatal(n)
				}
			}
			if n, ok := m.(*proto.SendMultipleActionNotice); ok {
				invite = n.ActionId == 10000 && n.FromPlayerId == 1 && n.EndTime == inv.endTime
			}
		}
		if !invite {
			t.Fatal("room observer missed invitation")
		}
	}
	// Exercise actual handler dispatch and default NONE status emitted by native client.
	g.MultipleActionAccept(p[1].Player, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 77}, Body: &proto.MultipleActionAcceptReq{FromPlayerId: 1, ActionId: 10000}})
	c.acceptMultipleAction(<-c.multipleAcceptChan, now)
	if lastAccept(t, conns[1]) != proto.StatusCode_StatusCode_OK {
		t.Fatal("accept failed")
	}
	for _, conn := range conns {
		var complete bool
		for _, m := range conn.messages {
			if n, ok := m.(*proto.SendMultipleActionCompleteNotice); ok {
				complete = n.ActionId == 10000 && n.FromPlayerId == 1 && len(n.ActionPlayerIds) == 2 && n.ActionPlayerIds[0] == 1 && n.ActionPlayerIds[1] == 2
			}
		}
		if !complete {
			t.Fatal("completion missing starter/requester or observer")
		}
	}
	acceptAction(c, p[2], 1, 10000, now)
	if lastAccept(t, conns[2]) != proto.StatusCode_StatusCode_MULTIPLE_ACTION_INVALID {
		t.Fatal("consumed invitation replay accepted")
	}
}
func TestMultipleActionValidationAndCancellation(t *testing.T) {
	_, c, p, conns := actionFixture(t)
	cfg := gdconf.GetAppearance().MultipleActions[10000]
	invite := func() { c.inviteMultipleAction(&ActionSyncCtx{ScenePlayer: p[0], ActionId: 10000}, cfg, 100) }
	invite()
	acceptAction(c, p[0], 1, 10000, 101)
	if lastAccept(t, conns[0]) == proto.StatusCode_StatusCode_OK {
		t.Fatal("self accept")
	}
	acceptAction(c, p[1], 1, 99999, 101)
	if lastAccept(t, conns[1]) == proto.StatusCode_StatusCode_OK {
		t.Fatal("wrong action")
	}
	other := &ChannelInfo{}
	p[1].channelInfo = other
	acceptAction(c, p[1], 1, 10000, 101)
	if lastAccept(t, conns[1]) == proto.StatusCode_StatusCode_OK {
		t.Fatal("cross channel target accepted")
	}
	p[1].channelInfo = c
	stale := &ScenePlayer{Player: &model.Player{UserId: 2, Conn: conns[1]}, channelInfo: c}
	acceptAction(c, stale, 1, 10000, 101)
	if lastAccept(t, conns[1]) == proto.StatusCode_StatusCode_OK {
		t.Fatal("stale session accepted")
	}
	acceptAction(c, p[1], 1, 10000, 130)
	if lastAccept(t, conns[1]) == proto.StatusCode_StatusCode_OK || len(c.multipleActions) != 0 {
		t.Fatal("deadline not enforced")
	}
	last := conns[2].messages[len(conns[2].messages)-1].(*proto.SendMultipleActionCompleteNotice)
	if last.ActionId != 0 || last.FromPlayerId != 1 {
		t.Fatal("missing native cancellation")
	}
	invite()
	c.SendActionNotice(&ActionSyncCtx{ScenePlayer: p[0], ActionId: 0})
	if len(c.multipleActions) != 0 {
		t.Fatal("action stop did not cancel")
	}
	invite()
	p[0].retired.Store(true)
	c.expireMultipleActions(101)
	if len(c.multipleActions) != 0 {
		t.Fatal("disconnect did not invalidate")
	}
	p[0].retired.Store(false)
	invite()
	p[0].channelInfo = other
	c.expireMultipleActions(101)
	if len(c.multipleActions) != 0 {
		t.Fatal("scene leave did not invalidate")
	}
}
func TestMultipleActionMoreThanTwoAndDuplicateAccept(t *testing.T) {
	_, c, p, conns := actionFixture(t)
	cfg := &excel.MultiplayerExpressionConfigure{PlayerNumber: 3}
	c.inviteMultipleAction(&ActionSyncCtx{ScenePlayer: p[0], ActionId: 10000}, cfg, 100)
	acceptAction(c, p[1], 1, 10000, 101)
	remaining := conns[3].messages[len(conns[3].messages)-1].(*proto.SendActionNotice)
	if remaining.MultipleNeedCount != 1 {
		t.Fatal("remaining participants not synchronized")
	}
	if len(c.multipleActions[1].participants) != 2 {
		t.Fatal("first requester missing")
	}
	acceptAction(c, p[1], 1, 10000, 101)
	if lastAccept(t, conns[1]) == proto.StatusCode_StatusCode_OK || len(c.multipleActions[1].participants) != 2 {
		t.Fatal("duplicate requester counted")
	}
	acceptAction(c, p[2], 1, 10000, 101)
	if len(c.multipleActions) != 0 {
		t.Fatal("three-player action not completed")
	}
	for _, m := range conns[3].messages {
		if n, ok := m.(*proto.SendMultipleActionCompleteNotice); ok && n.ActionId == 10000 {
			if len(n.ActionPlayerIds) != 3 {
				t.Fatal(n)
			}
			return
		}
	}
	t.Fatal("observer missing three-player completion")
}
func TestMultipleActionCompletedGroupStopAndLeave(t *testing.T) {
	_, c, p, conns := actionFixture(t)
	cfg := gdconf.GetAppearance().MultipleActions[10000]
	complete := func() {
		c.inviteMultipleAction(&ActionSyncCtx{ScenePlayer: p[0], ActionId: 10000}, cfg, 100)
		acceptAction(c, p[1], 1, 10000, 101)
		if len(c.activeMultipleActions) != 1 {
			t.Fatal("completed relation missing")
		}
	}
	assertStop := func() {
		t.Helper()
		if len(c.activeMultipleActions) != 0 {
			t.Fatal("active group retained")
		}
		n := conns[3].messages[len(conns[3].messages)-1].(*proto.SendMultipleActionCompleteNotice)
		if n.ActionId != 0 || n.FromPlayerId != 1 {
			t.Fatal("missing group StopLooping")
		}
	}
	complete()
	c.expireMultipleActions(1000)
	if len(c.activeMultipleActions) != 1 {
		t.Fatal("loop incorrectly expires at invite deadline")
	}
	c.cancelPlayerMultipleActions(p[1])
	assertStop()
	complete()
	c.SendActionNotice(&ActionSyncCtx{ScenePlayer: p[0], ActionId: 0})
	if len(c.activeMultipleActions) != 0 {
		t.Fatal("starter action0 did not stop group")
	}
	var stop bool
	for _, m := range conns[3].messages {
		if n, ok := m.(*proto.SendMultipleActionCompleteNotice); ok && n.ActionId == 0 && n.FromPlayerId == 1 {
			stop = true
		}
	}
	if !stop {
		t.Fatal("starter stop not broadcast")
	}
	complete()
	p[1].retired.Store(true)
	c.expireMultipleActions(102)
	assertStop()
	p[1].retired.Store(false)
	complete()
	replacement := &ScenePlayer{Player: p[1].Player, channelInfo: c}
	c.allPlayer[2] = replacement
	c.expireMultipleActions(102)
	assertStop()
}
func TestMultipleActionRejectUnknownSendAndClosedRoom(t *testing.T) {
	g, c, p, conns := actionFixture(t)
	g.SendAction(p[0].Player, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 1}, Body: &proto.SendActionReq{ActionId: 999999}})
	if conns[0].messages[0].(*proto.SendActionRsp).Status != proto.StatusCode_StatusCode_BAD_REQ || len(c.actionSyncChan) != 0 {
		t.Fatal("unknown action forwarded")
	}
	close(c.doneChan)
	c.multipleAcceptChan = nil
	g.MultipleActionAccept(p[0].Player, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 2}, Body: &proto.MultipleActionAcceptReq{FromPlayerId: 2, ActionId: 10000}})
	if lastAccept(t, conns[0]) != proto.StatusCode_StatusCode_PLAYER_NOT_IN_CHANNEL {
		t.Fatal("closed room did not respond")
	}
}
