package game

import (
	"sync"
	"testing"
	"time"

	"github.com/gookit/slog"
	pb "google.golang.org/protobuf/proto"
	"gucooing/lolo/game/model"
	"gucooing/lolo/pkg/log"
	"gucooing/lolo/pkg/ofnet"
	"gucooing/lolo/protocol/cmd"
	"gucooing/lolo/protocol/proto"
)

type chatCaptureConn struct {
	ofnet.Conn
	joined   chan struct{}
	messages chan *proto.ChatMsgNotice
}

func newChatCaptureConn() *chatCaptureConn {
	return &chatCaptureConn{joined: make(chan struct{}, 10), messages: make(chan *proto.ChatMsgNotice, 10)}
}
func (c *chatCaptureConn) Send(_ uint32, msg pb.Message) {
	if _, ok := msg.(*proto.ChangeChatChannelNotice); ok {
		c.joined <- struct{}{}
	}
}
func (c *chatCaptureConn) SendBin(_ uint32, id uint32, body []byte) {
	if id != cmd.ChatMsgNotice {
		panic("unexpected broadcast command")
	}
	msg := new(proto.ChatMsgNotice)
	if err := pb.Unmarshal(body, msg); err != nil {
		panic(err)
	}
	c.messages <- msg
}
func chatWaitJoin(t *testing.T, c *chatCaptureConn) {
	t.Helper()
	select {
	case <-c.joined:
	case <-time.After(time.Second):
		t.Fatal("chat loop did not process join")
	}
}
func chatLogger(t *testing.T) {
	old := log.Game
	log.Game = slog.NewStdLogger()
	t.Cleanup(func() { log.Game = old })
}

func TestChatChannelNewPlayerSameUIDReceivesBroadcast(t *testing.T) {
	chatLogger(t)
	for _, scene := range []bool{false, true} {
		t.Run(map[bool]string{false: "public", true: "scene"}[scene], func(t *testing.T) {
			info := new(ChatInfo)
			channel := newChatChannel()
			channel.Type = proto.ChatChannelType_ChatChannel_ChatRoom
			done := make(chan struct{})
			go func() { defer close(done); channel.channelMainLoop() }()
			defer func() { channel.Close(); <-done }()
			get := info.getChannelUser
			if scene {
				get = info.getChannelSceneUser
			}
			oldConn, newConn, peerConn := newChatCaptureConn(), newChatCaptureConn(), newChatCaptureConn()
			oldPlayer := &model.Player{UserId: 42, Conn: oldConn}
			newPlayer := &model.Player{UserId: 42, Conn: newConn}
			peer := &model.Player{UserId: 77, Conn: peerConn}
			oldUser := get(oldPlayer)
			channel.addUserChan <- oldUser
			chatWaitJoin(t, oldConn)
			channel.addUserChan <- get(peer)
			chatWaitJoin(t, peerConn)
			newUser := get(newPlayer)
			if newUser == oldUser || oldUser.Player != oldPlayer {
				t.Fatal("new login reused or mutated old session")
			}
			channel.addUserChan <- newUser
			chatWaitJoin(t, newConn)
			channel.allSendMsgChan <- &proto.ChatMsgData{PlayerId: 77, Text: "to new session"}
			select {
			case msg := <-newConn.messages:
				if msg.Msg.Text != "to new session" {
					t.Fatalf("wrong message: %v", msg)
				}
			case <-time.After(time.Second):
				t.Fatal("broadcast did not reach new connection")
			}
			select {
			case <-oldConn.messages:
				t.Fatal("broadcast sent to old connection")
			default:
			}
			// Retiring the old Player cannot remove the replacement cache entry.
			info.killChannelUser(oldPlayer)
			if get(newPlayer) != newUser {
				t.Fatal("old kill removed new cache entry")
			}
			info.killChannelUser(newPlayer)
			cache := &info.allChannelUser
			if scene {
				cache = &info.allChannelSceneUser
			}
			if _, ok := cache.Load(uint32(42)); ok {
				t.Fatal("kill retained session cache")
			}
		})
	}
}

func TestChatChannelOldLeaveDoesNotDeleteNewSession(t *testing.T) {
	chatLogger(t)
	info := new(ChatInfo)
	c := newChatChannel()
	oldPlayer := &model.Player{UserId: 42, Conn: newChatCaptureConn()}
	newPlayer := &model.Player{UserId: 42, Conn: newChatCaptureConn()}
	peer := &model.Player{UserId: 77, Conn: newChatCaptureConn()}
	oldUser := info.getChannelSceneUser(oldPlayer)
	c.addUser(oldUser)
	newUser := info.getChannelSceneUser(newPlayer)
	c.addUser(newUser)
	c.addUser(info.getChannelSceneUser(peer))
	c.delUser(oldUser)
	if c.userNum != 2 || c.userMap[42].Player != newPlayer {
		t.Fatal("old leave removed replacement or changed count")
	}
	c.allSendMsg(&proto.ChatMsgData{PlayerId: 77, Text: "after stale leave"})
	select {
	case <-newPlayer.Conn.(*chatCaptureConn).messages:
	default:
		t.Fatal("new connection lost broadcasts after old leave")
	}
	c.delUser(newUser)
	c.delUser(newUser)
	if c.userNum != 1 || c.userMap[42] != nil {
		t.Fatal("current leave not idempotent")
	}
}

func TestChatKillClearsBothCachesForOfflinePlayer(t *testing.T) {
	chatLogger(t)
	p := &model.Player{UserId: 42, Conn: newChatCaptureConn(), Online: false}
	g := &Game{userMap: map[uint32]*model.Player{42: p}}
	info := g.getChatInfo()
	info.getChannelUser(p)
	info.getChannelSceneUser(p)
	sp := &ScenePlayer{Player: p}
	g.getWordInfo().allScenePlayer.Store(p.UserId, sp)
	g.kickPlayer(p)
	for _, cache := range []*sync.Map{&info.allChannelUser, &info.allChannelSceneUser, &g.wordInfo.allScenePlayer} {
		if _, ok := cache.Load(p.UserId); ok {
			t.Fatal("offline eviction retained session")
		}
	}
	newer := &model.Player{UserId: 42}
	newerScene := &ScenePlayer{Player: newer}
	g.wordInfo.allScenePlayer.Store(newer.UserId, newerScene)
	g.wordInfo.killScenePlayer(p)
	if got, _ := g.wordInfo.allScenePlayer.Load(newer.UserId); got != newerScene {
		t.Fatal("old scene kill removed new session")
	}
}

func TestChatSamePlayerNewConnectionAndQueuedEviction(t *testing.T) {
	chatLogger(t)
	info := new(ChatInfo)
	c := newChatChannel()
	oldConn, newConn := newChatCaptureConn(), newChatCaptureConn()
	p := &model.Player{UserId: 42, Conn: oldConn}
	oldUser := info.getChannelUser(p)
	c.addUser(oldUser)
	p.Conn = newConn
	newUser := info.getChannelUser(p)
	c.addUser(newUser)
	c.delUser(oldUser)
	if c.userMap[42] != newUser || c.userNum != 1 {
		t.Fatal("same Player reconnect lost membership to old connection leave")
	}
	// An old add that was queued before replacement/eviction must stay retired.
	c.addUser(oldUser)
	if c.userMap[42] != newUser {
		t.Fatal("queued old add overwrote new connection")
	}
	info.killChannelUser(p)
	c.delUser(newUser)
	c.addUser(newUser)
	if c.userMap[42] != nil || c.userNum != 0 {
		t.Fatal("queued add resurrected evicted session")
	}
	// A scene add queued before game eviction must not recreate either cache.
	sp := &ScenePlayer{Player: p}
	w := new(WordInfo)
	w.allScenePlayer.Store(p.UserId, sp)
	w.killScenePlayer(p)
	if (&ChannelInfo{}).addPlayer(sp) {
		t.Fatal("queued scene add resurrected evicted session")
	}
}

func TestSceneOldLeavePreservesNewSession(t *testing.T) {
	g := new(Game)
	c := &ChannelInfo{game: g, chatChannel: newChatChannel(), allPlayer: make(map[uint32]*ScenePlayer), playerNum: 1}
	oldPlayer := &ScenePlayer{Player: &model.Player{UserId: 42, Conn: newChatCaptureConn()}}
	newer := &ScenePlayer{Player: &model.Player{UserId: 42, Conn: newChatCaptureConn()}}
	c.allPlayer[42] = newer
	cached := g.getChatInfo().getChannelSceneUser(newer.Player)
	cached.channel.Store(c.chatChannel)
	oldUser := &ChannelUser{Player: oldPlayer.Player, conn: oldPlayer.Conn}
	c.chatUsers = map[*ScenePlayer]*ChannelUser{oldPlayer: oldUser}
	c.delPlayer(oldPlayer)
	if c.allPlayer[42] != newer || c.playerNum != 1 {
		t.Fatal("stale scene leave removed new session")
	}
	if got, _ := g.chatInfo.allChannelSceneUser.Load(uint32(42)); got != cached {
		t.Fatal("stale scene leave removed new cache")
	}
	if leave := <-c.chatChannel.delUserChan; leave != oldUser {
		t.Fatal("scene leave lost identity")
	}
}
