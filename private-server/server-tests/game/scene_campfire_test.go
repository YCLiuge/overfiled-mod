package game

import (
	"math"
	"testing"

	pb "google.golang.org/protobuf/proto"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/protocol/cmd"
	"gucooing/lolo/protocol/proto"
)

type campFireCaptureConn struct{ *actionCaptureConn }

func (c *campFireCaptureConn) SendBin(id, command uint32, body []byte) {
	var msg pb.Message
	switch command {
	case cmd.PlaceCampFireNotice:
		msg = new(proto.PlaceCampFireNotice)
	case cmd.RemoveCampFireNotice:
		msg = new(proto.RemoveCampFireNotice)
	default:
		c.actionCaptureConn.SendBin(id, command, body)
		return
	}
	if err := pb.Unmarshal(body, msg); err != nil {
		panic(err)
	}
	c.Send(id, msg)
}
func campFireFixture(t *testing.T) (*Game, *ChannelInfo, []*ScenePlayer, []*actionCaptureConn) {
	g, c, p, conns := actionFixture(t)
	c.campFireChan = make(chan *campFireCtx, 10)
	for i, sp := range p {
		sp.Conn = &campFireCaptureConn{conns[i]}
	}
	return g, c, p, conns
}
func campPlaceReq(action uint32) *proto.PlaceCampFireReq {
	return &proto.PlaceCampFireReq{ActionId: action, Pos: &proto.Vector3{X: -1234, Y: 5678, Z: 9012}, Rot: &proto.Vector3{X: 100, Y: 23400, Z: -30}}
}
func campLastPlace(t *testing.T, conn *actionCaptureConn) *proto.PlaceCampFireRsp {
	t.Helper()
	for i := len(conn.messages) - 1; i >= 0; i-- {
		if r, ok := conn.messages[i].(*proto.PlaceCampFireRsp); ok {
			return r
		}
	}
	t.Fatal("no place response")
	return nil
}
func campLastRemove(t *testing.T, conn *actionCaptureConn) *proto.RemoveCampFireRsp {
	t.Helper()
	for i := len(conn.messages) - 1; i >= 0; i-- {
		if r, ok := conn.messages[i].(*proto.RemoveCampFireRsp); ok {
			return r
		}
	}
	t.Fatal("no remove response")
	return nil
}
func TestCampFirePlaceAndNativeSceneProjection(t *testing.T) {
	g, c, p, conns := campFireFixture(t)
	g.newRouter()
	if g.handlerFuncRouteMap[cmd.PlaceCampFireReq] == nil || g.handlerFuncRouteMap[cmd.RemoveCampFireReq] == nil {
		t.Fatal("missing routes")
	}
	req := campPlaceReq(1050)
	g.PlaceCampFire(p[0].Player, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 41}, Body: req})
	if len(c.campFires) != 0 {
		t.Fatal("route mutated channel state")
	}
	ctx := <-c.campFireChan
	req.Pos.X = 999 // queued payload must not alias the route caller
	c.placeCampFire(ctx, 1700000000)
	rsp := campLastPlace(t, conns[0])
	fire := rsp.CampFire
	if rsp.Status != proto.StatusCode_StatusCode_OK || fire.Id == 0 || fire.PlayerId != 1 || fire.ActionId != 1050 || fire.PlaceTime != 1700000000 || fire.Pos.X != -1234 || fire.Rot.Y != 23400 {
		t.Fatalf("wire fields lost: %v", rsp)
	}
	if conns[0].packets[len(conns[0].packets)-1] != 41 {
		t.Fatal("response packet id lost")
	}
	for _, conn := range conns {
		if n, ok := conn.messages[0].(*proto.PlaceCampFireNotice); !ok || len(n.CampFires) != 1 || !pb.Equal(n.CampFires[0], fire) {
			t.Fatal("missing room notice")
		}
	}
	modern, legacy := c.campFireSceneData()
	if len(modern) != 1 || len(legacy) != 1 || legacy[0].Id != int32(fire.Id) || legacy[0].PlayerId != 1 || !pb.Equal(legacy[0].Position, fire.Pos) || !pb.Equal(legacy[0].Rotation, fire.Rot) {
		t.Fatal("legacy native scene projection incorrect")
	}
	modern[0].Pos.X = 0
	legacy[0].Rotation.Y = 0
	if c.campFires[fire.Id].data.Pos.X != -1234 || c.campFires[fire.Id].data.Rot.Y != 23400 {
		t.Fatal("snapshot aliases channel state")
	}
}
func TestCampFireRemoveOwnershipReplacementAndLeave(t *testing.T) {
	_, c, p, conns := campFireFixture(t)
	c.placeCampFire(&campFireCtx{player: p[0], place: campPlaceReq(1051)}, 1)
	first := campLastPlace(t, conns[0]).CampFire.Id
	c.removeCampFire(&campFireCtx{player: p[1], remove: &proto.RemoveCampFireReq{CampFireId: first}})
	if campLastRemove(t, conns[1]).Status == proto.StatusCode_StatusCode_OK || len(c.campFires) != 1 {
		t.Fatal("foreign fire removed")
	}
	c.placeCampFire(&campFireCtx{player: p[0], place: campPlaceReq(1052)}, 2)
	second := campLastPlace(t, conns[0]).CampFire.Id
	if first == second || len(c.campFires) != 1 || c.campFires[first] != nil {
		t.Fatal("replacement leaked prior fire")
	}
	c.removeCampFire(&campFireCtx{player: p[0], packetID: 72, remove: &proto.RemoveCampFireReq{CampFireId: second}})
	if campLastRemove(t, conns[0]).Status != proto.StatusCode_StatusCode_OK || len(c.campFires) != 0 {
		t.Fatal("owner remove failed")
	}
	c.placeCampFire(&campFireCtx{player: p[0], place: campPlaceReq(1050)}, 3)
	// Late old-session leave still cleans its fire without deleting new membership.
	replacement := &ScenePlayer{Player: p[0].Player, channelInfo: c}
	c.allPlayer[1] = replacement
	c.delPlayer(p[0])
	if len(c.campFires) != 0 || c.allPlayer[1] != replacement {
		t.Fatal("old-session cleanup failed")
	}
	c.placeCampFire(&campFireCtx{player: replacement, place: campPlaceReq(1050)}, 4)
	c.cancelPlayerCampFires(p[0])
	if len(c.campFires) != 1 {
		t.Fatal("old leave deleted replacement fire")
	}
	c.cancelPlayerCampFires(replacement)
	if len(c.campFires) != 0 {
		t.Fatal("leave leaked fire")
	}
}
func TestCampFireRejectInvalidStaleAndClosed(t *testing.T) {
	g, c, p, conns := campFireFixture(t)
	for _, action := range []uint32{0, 101, 999999} {
		c.placeCampFire(&campFireCtx{player: p[0], place: campPlaceReq(action)}, 1)
		if campLastPlace(t, conns[0]).Status != proto.StatusCode_StatusCode_BAD_REQ || len(c.campFires) != 0 {
			t.Fatalf("accepted action %d", action)
		}
	}
	req := campPlaceReq(1050)
	req.Rot = nil
	c.placeCampFire(&campFireCtx{player: p[0], place: req}, 1)
	if campLastPlace(t, conns[0]).Status != proto.StatusCode_StatusCode_BAD_REQ {
		t.Fatal("accepted absent rotation")
	}
	p[0].retired.Store(true)
	c.placeCampFire(&campFireCtx{player: p[0], place: campPlaceReq(1050)}, 1)
	if campLastPlace(t, conns[0]).Status != proto.StatusCode_StatusCode_PLAYER_NOT_IN_CHANNEL {
		t.Fatal("accepted stale member")
	}
	p[0].retired.Store(false)
	c.nextCampFireID = math.MaxInt32
	c.placeCampFire(&campFireCtx{player: p[0], place: campPlaceReq(1050)}, 1)
	if campLastPlace(t, conns[0]).CampFire.Id != 1 {
		t.Fatal("id must fit legacy positive int32")
	}
	close(c.doneChan)
	g.PlaceCampFire(p[0].Player, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 81}, Body: campPlaceReq(1050)})
	g.RemoveCampFire(p[0].Player, &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: 82}, Body: &proto.RemoveCampFireReq{CampFireId: 1}})
	if len(c.campFireChan) != 0 || campLastPlace(t, conns[0]).Status != proto.StatusCode_StatusCode_PLAYER_NOT_IN_CHANNEL || campLastRemove(t, conns[0]).Status != proto.StatusCode_StatusCode_PLAYER_NOT_IN_CHANNEL {
		t.Fatal("closed channel accepted request")
	}
}
