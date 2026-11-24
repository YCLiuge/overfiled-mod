package logserver

import (
	"testing"
	"time"

	pb "google.golang.org/protobuf/proto"

	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/ofnet"
	"gucooing/lolo/protocol/proto"
)

// No socket is opened: PlayerPing must only call Send on this connection.
// Other methods deliberately remain unset so an unexpected dependency fails.
type pingCaptureConn struct {
	ofnet.Conn
	packetID uint32
	message  pb.Message
	sends    int
}

func (c *pingCaptureConn) Send(packetID uint32, message pb.Message) {
	c.packetID = packetID
	c.message = message
	c.sends++
}

func TestPlayerPing(t *testing.T) {
	for _, tc := range []struct {
		name       string
		packetID   uint32
		clientTime int64
	}{
		{name: "zero packet and client clock", packetID: 0, clientTime: 0},
		{name: "old client clock", packetID: 456, clientTime: 123},
		{name: "future client clock", packetID: ^uint32(0), clientTime: 4102444800000},
	} {
		t.Run(tc.name, func(t *testing.T) {
			conn := &pingCaptureConn{}
			req := &proto.PlayerPingReq{ClientTimeMs: tc.clientTime}
			msg := &alg.GameMsg{PacketHead: &proto.PacketHead{PacketId: tc.packetID}, Body: req}

			before := time.Now().UnixMilli()
			new(LogServer).PlayerPing(conn, msg)
			after := time.Now().UnixMilli()

			if conn.sends != 1 {
				t.Fatalf("sent %d messages, want exactly one", conn.sends)
			}
			if conn.packetID != tc.packetID {
				t.Errorf("packet ID = %d, want %d", conn.packetID, tc.packetID)
			}
			rsp, ok := conn.message.(*proto.PlayerPingRsp)
			if !ok {
				t.Fatalf("response type = %T, want *proto.PlayerPingRsp", conn.message)
			}
			if rsp.Status != proto.StatusCode_StatusCode_OK {
				t.Errorf("status = %v, want OK", rsp.Status)
			}
			if rsp.ClientTimeMs != tc.clientTime {
				t.Errorf("client time = %d, want %d", rsp.ClientTimeMs, tc.clientTime)
			}
			if rsp.ServerTimeMs < before || rsp.ServerTimeMs > after {
				t.Errorf("server time = %d, want [%d, %d]", rsp.ServerTimeMs, before, after)
			}
			if req.ClientTimeMs != tc.clientTime {
				t.Errorf("request client time changed to %d", req.ClientTimeMs)
			}
		})
	}
}
