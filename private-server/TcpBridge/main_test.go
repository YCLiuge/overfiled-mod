package main

import (
	"bufio"
	"bytes"
	"context"
	"encoding/binary"
	"io"
	"net"
	"net/http"
	"strings"
	"sync/atomic"
	"testing"
	"time"
)

var testKey = bytes.Repeat([]byte{0x42}, 32)

func startTunnel(t *testing.T, dial dialFunc) (string, func()) {
	t.Helper()
	l, e := net.Listen("tcp", "127.0.0.1:0")
	if e != nil {
		t.Fatal(e)
	}
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan struct{})
	go func() {
		defer close(done)
		_ = serve(ctx, l, func(c net.Conn) { _ = serverConnection(ctx, c, testKey, dial) })
	}()
	return l.Addr().String(), func() {
		cancel()
		select {
		case <-done:
		case <-time.After(3 * time.Second):
			t.Error("server shutdown leaked connection")
		}
	}
}
func echoBackend(t *testing.T) (string, func()) {
	t.Helper()
	l, e := net.Listen("tcp", "127.0.0.1:0")
	if e != nil {
		t.Fatal(e)
	}
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan struct{})
	go func() { defer close(done); _ = serve(ctx, l, func(c net.Conn) { io.Copy(c, c) }) }()
	return l.Addr().String(), func() { cancel(); <-done }
}
func connect(t *testing.T, addr, route string, key []byte, probe bool) *recordIO {
	t.Helper()
	c, e := net.DialTimeout("tcp", addr, time.Second)
	if e != nil {
		t.Fatal(e)
	}
	s, e := clientHandshake(c, key, route, probe)
	if e != nil {
		c.Close()
		t.Fatal(e)
	}
	return s
}
func TestThreeRoutesAndHTTPProxy(t *testing.T) {
	echo, closeEcho := echoBackend(t)
	defer closeEcho()
	hl, e := net.Listen("tcp", "127.0.0.1:0")
	if e != nil {
		t.Fatal(e)
	}
	hs := &http.Server{Handler: http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/proxy-check" {
			t.Error("unexpected path")
		}
		w.Write([]byte("HTTP through encrypted TCP"))
	})}
	go hs.Serve(hl)
	defer hs.Close()
	var calls atomic.Int32
	remote, stop := startTunnel(t, func(ctx context.Context, network, addr string) (net.Conn, error) {
		calls.Add(1)
		target := echo
		if addr == backends["http"] {
			target = hl.Addr().String()
		} else if addr != backends["game"] && addr != backends["log"] {
			t.Error("arbitrary backend", addr)
		}
		return defaultDial(ctx, network, target)
	})
	defer stop()
	for _, route := range []string{"game", "log"} {
		t.Run(route, func(t *testing.T) {
			s := connect(t, remote, route, testKey, false)
			defer s.Close()
			s.SetDeadline(time.Now().Add(3 * time.Second))
			payload := bytes.Repeat([]byte("echo-"+route), 16000)
			done := make(chan error, 1)
			go func() {
				_, e := s.Write(payload)
				if e == nil {
					e = s.CloseWrite()
				}
				done <- e
			}()
			got, e := io.ReadAll(s)
			if e != nil {
				t.Fatal(e)
			}
			if !bytes.Equal(got, payload) {
				t.Fatal("echo mismatch")
			}
			if e = <-done; e != nil {
				t.Fatal(e)
			}
		})
	}
	s := connect(t, remote, "http", testKey, false)
	defer s.Close()
	s.SetDeadline(time.Now().Add(3 * time.Second))
	io.WriteString(s, "GET /proxy-check HTTP/1.1\r\nHost: test\r\nConnection: close\r\n\r\n")
	resp, e := http.ReadResponse(bufio.NewReader(s), nil)
	if e != nil {
		t.Fatal(e)
	}
	body, e := io.ReadAll(resp.Body)
	resp.Body.Close()
	if e != nil || string(body) != "HTTP through encrypted TCP" {
		t.Fatalf("HTTP body %q: %v", body, e)
	}
	if e = probeRemote(context.Background(), remote, testKey); e != nil {
		t.Fatal(e)
	}
	if calls.Load() != 6 {
		t.Fatalf("backend dial count %d", calls.Load())
	}
}
func TestWrongKeyAndUnknownRouteNeverDial(t *testing.T) {
	var calls atomic.Int32
	remote, stop := startTunnel(t, func(context.Context, string, string) (net.Conn, error) { calls.Add(1); return nil, io.EOF })
	defer stop()
	for _, tc := range []struct {
		route string
		key   []byte
	}{{"http", bytes.Repeat([]byte{1}, 32)}, {"evil", testKey}} {
		c, e := net.Dial("tcp", remote)
		if e != nil {
			t.Fatal(e)
		}
		_, e = clientHandshake(c, tc.key, tc.route, false)
		c.Close()
		if e == nil {
			t.Fatal("invalid authentication/route accepted")
		}
	}
	if calls.Load() != 0 {
		t.Fatal("dial before route authentication")
	}
}
func TestProbeBackendFailure(t *testing.T) {
	remote, stop := startTunnel(t, func(context.Context, string, string) (net.Conn, error) { return nil, io.EOF })
	defer stop()
	if e := probeRemote(context.Background(), remote, testKey); e == nil {
		t.Fatal("probe ignored unavailable backend")
	}
}
func TestRecordTamperAndLength(t *testing.T) {
	for _, kind := range []string{"tamper", "oversize", "short", "truncated"} {
		t.Run(kind, func(t *testing.T) {
			a, b := net.Pipe()
			defer a.Close()
			defer b.Close()
			ae := derive(testKey, make([]byte, 32), make([]byte, 32), "test")
			r := &recordIO{Conn: b, recv: ae}
			go func() {
				defer a.Close()
				var h [4]byte
				switch kind {
				case "tamper":
					binary.BigEndian.PutUint32(h[:], 18)
					data := ae.Seal(nil, nonce([4]byte{}, 0), []byte{1, 42}, h[:])
					data[len(data)-1] ^= 1
					writeAll(a, h[:])
					writeAll(a, data)
				case "oversize":
					binary.BigEndian.PutUint32(h[:], maxPayload+18)
					writeAll(a, h[:])
				case "short":
					binary.BigEndian.PutUint32(h[:], 16)
					writeAll(a, h[:])
				case "truncated":
					binary.BigEndian.PutUint32(h[:], 20)
					writeAll(a, h[:])
					writeAll(a, []byte{1})
				}
			}()
			b.SetReadDeadline(time.Now().Add(time.Second))
			if _, e := r.recvRecord(); e == nil {
				t.Fatal("bad record accepted")
			}
		})
	}
}
func TestChallengeReplayRejected(t *testing.T) {
	var calls atomic.Int32
	remote, stop := startTunnel(t, func(context.Context, string, string) (net.Conn, error) { calls.Add(1); return nil, io.EOF })
	defer stop()
	c, _ := net.Dial("tcp", remote)
	hello := make([]byte, 44)
	io.ReadFull(c, hello)
	c.Close()
	salt := bytes.Repeat([]byte{7}, 36)
	ae := derive(testKey, hello[8:40], salt[:32], "c2s")
	var prefix [4]byte
	copy(prefix[:], salt[32:])
	request := []byte{3, 0, 'h', 't', 't', 'p'}
	var hdr [4]byte
	binary.BigEndian.PutUint32(hdr[:], uint32(len(request)+16))
	record := ae.Seal(nil, nonce(prefix, 0), request, hdr[:])
	c, _ = net.Dial("tcp", remote)
	defer c.Close()
	io.ReadFull(c, make([]byte, 44))
	writeAll(c, salt)
	writeAll(c, hdr[:])
	writeAll(c, record)
	c.SetReadDeadline(time.Now().Add(time.Second))
	if _, e := c.Read(make([]byte, 1)); e == nil {
		t.Fatal("replay accepted")
	}
	if calls.Load() != 0 {
		t.Fatal("replay dialed backend")
	}
}
func TestIndependentDirectionalKeys(t *testing.T) {
	a := derive(testKey, make([]byte, 32), make([]byte, 32), "c2s")
	b := derive(testKey, make([]byte, 32), make([]byte, 32), "s2c")
	n := make([]byte, 12)
	ct := a.Seal(nil, n, []byte("payload"), nil)
	if _, e := b.Open(nil, n, ct, nil); e == nil {
		t.Fatal("direction keys identical")
	}
}
func TestClientListeners(t *testing.T) {
	echo, closeEcho := echoBackend(t)
	defer closeEcho()
	remote, stop := startTunnel(t, func(ctx context.Context, n, a string) (net.Conn, error) { return defaultDial(ctx, n, echo) })
	defer stop()
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() { done <- run(ctx, Config{Mode: "client", Remote: remote}, testKey, false) }()
	defer func() {
		cancel()
		select {
		case e := <-done:
			if e != nil {
				t.Error(e)
			}
		case <-time.After(3 * time.Second):
			t.Error("client shutdown leaked")
		}
	}()
	for route, addr := range frontends {
		var c net.Conn
		var e error
		deadline := time.Now().Add(2 * time.Second)
		for time.Now().Before(deadline) {
			c, e = net.DialTimeout("tcp", addr, 100*time.Millisecond)
			if e == nil {
				break
			}
			time.Sleep(10 * time.Millisecond)
		}
		if e != nil {
			t.Fatal(e)
		}
		c.SetDeadline(time.Now().Add(2 * time.Second))
		msg := "loopback-" + route
		io.WriteString(c, msg)
		got := make([]byte, len(msg))
		_, e = io.ReadFull(c, got)
		c.Close()
		if e != nil || string(got) != msg {
			t.Fatalf("%s: %s %v", route, got, e)
		}
	}
}
func TestStalledHandshakeCancelled(t *testing.T) {
	remote, stop := startTunnel(t, defaultDial)
	c, err := net.Dial("tcp", remote)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	hello := make([]byte, 44)
	if _, err = io.ReadFull(c, hello); err != nil {
		t.Fatal(err)
	}
	// Client sends nothing; cancellation must terminate the blocked handshake.
	stop()
	c.SetReadDeadline(time.Now().Add(time.Second))
	if _, err = c.Read(make([]byte, 1)); err == nil {
		t.Fatal("stalled handshake remained open")
	}
}

func TestNoPlainHTTPProtocol(t *testing.T) {
	remote, stop := startTunnel(t, defaultDial)
	defer stop()
	c, _ := net.Dial("tcp", remote)
	defer c.Close()
	b := make([]byte, 44)
	io.ReadFull(c, b)
	if strings.HasPrefix(string(b), "HTTP") {
		t.Fatal("HTTP transport detected")
	}
}
