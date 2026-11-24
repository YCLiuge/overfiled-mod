package game

import (
	"github.com/gookit/slog"
	pb "google.golang.org/protobuf/proto"
	"gucooing/lolo/game/model"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/pkg/ofnet"
	"gucooing/lolo/protocol/proto"
	"testing"
	"time"
)

type captureConn struct {
	ofnet.Conn
	packets []uint32
	bodies  []pb.Message
}

func (c *captureConn) Send(id uint32, m pb.Message) {
	c.packets = append(c.packets, id)
	c.bodies = append(c.bodies, pb.Clone(m))
}

func TestLocalMissingReconnectRepliesOnce(t *testing.T) {
	c := new(captureConn)
	g := &Game{userMap: make(map[uint32]*model.Player)}
	g.PlayerLogin(c, 42, "test", &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 17}, Body: &proto.PlayerLoginReq{IsReconnect: true}})
	if len(c.bodies) != 1 || c.packets[0] != 17 {
		t.Fatalf("missing or duplicate login response: %v", c.packets)
	}
	rsp := c.bodies[0].(*proto.PlayerLoginRsp)
	if rsp.ReconnectSuccess || rsp.IsReconnect {
		t.Fatal("missing player accepted as reconnect")
	}
}

func TestLocalEmptySceneRecordDoesNotKillGame(t *testing.T) {
	c := new(captureConn)
	p := &model.Player{Conn: c}
	g := new(Game)
	g.PlayerSceneRecord(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 18}, Body: &proto.PlayerSceneRecordReq{}})
	if c.bodies[0].(*proto.PlayerSceneRecordRsp).Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("empty record accepted")
	}
	// Processing another message after the bad record must still work.
	g.PlayerPing(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 19}, Body: &proto.PlayerPingReq{ClientTimeMs: 123}})
	if len(c.bodies) != 2 || c.bodies[1].(*proto.PlayerPingRsp).ClientTimeMs != 123 {
		t.Fatal("ping after malformed record failed")
	}
}

func TestLocalChangeSceneResponseMatchesDestination(t *testing.T) {
	oldLogger := log.Game
	log.Game = slog.NewStdLogger()
	defer func() { log.Game = oldLogger }()
	for _, tc := range []struct {
		name                   string
		scene, channel, target uint32
		fail                   bool
	}{
		{"current scene", 0, 0, 0, false}, {"another room", 1, 2, 0, false}, {"friend destination", 0, 0, 77, false}, {"missing friend", 0, 0, 999, true}, {"invalid room", 1, 100000, 0, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			c := new(captureConn)
			p := &model.Player{UserId: 42, Conn: c}
			g := new(Game)
			w := g.getWordInfo()
			w.allScene = make(map[uint32]*SceneInfo)
			room := func(id uint32) *ChannelInfo {
				return &ChannelInfo{ChannelId: id, addScenePlayerChan: make(chan *ScenePlayer, 1), delScenePlayerChan: make(chan *ScenePlayer, 1)}
			}
			r1, r2, rf := room(1), room(2), room(3)
			w.allScene[1] = &SceneInfo{allChannel: map[uint32]*ChannelInfo{1: r1, 2: r2}}
			w.allScene[2] = &SceneInfo{allChannel: map[uint32]*ChannelInfo{3: rf}}
			scene := func(owner *model.Player, id uint32) *model.ScenePlayerInfo {
				return &model.ScenePlayerInfo{Player: owner, SceneId: id, Pos: &proto.Vector3{X: 4}, Rot: &proto.Vector3{}, Team: &model.TeamInfo{}}
			}
			sp := &ScenePlayer{Player: p, ChannelId: 1, channelInfo: r1, CurScene: scene(p, 1)}
			w.allScenePlayer.Store(p.UserId, sp)
			friend := &model.Player{UserId: 77}
			w.allScenePlayer.Store(uint32(77), &ScenePlayer{Player: friend, ChannelId: 3, channelInfo: rf, CurScene: scene(friend, 2)})
			done := make(chan struct{})
			go func() {
				g.ChangeSceneChannel(p, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 20}, Body: &proto.ChangeSceneChannelReq{SceneId: tc.scene, ChannelLabel: tc.channel, TargetPlayerLabel: tc.target, Pos: &proto.Vector3{}, Rot: &proto.Vector3{}}})
				close(done)
			}()
			select {
			case <-done:
			case <-time.After(time.Second):
				t.Fatal("scene transition blocked")
			}
			rsp := c.bodies[0].(*proto.ChangeSceneChannelRsp)
			if tc.fail {
				if rsp.Status == proto.StatusCode_StatusCode_OK || sp.ChannelId != 1 || sp.CurScene.GetSceneId() != 1 {
					t.Fatalf("invalid destination changed state: %v", rsp)
				}
				return
			}
			if rsp.Status != proto.StatusCode_StatusCode_OK || rsp.SceneId != sp.CurScene.GetSceneId() || rsp.ChannelId != sp.ChannelId || rsp.ChannelLabel != sp.ChannelId || rsp.TargetPlayerId != tc.target {
				t.Fatalf("response disagrees with actual destination: %v", rsp)
			}
		})
	}
}
