package sdk

import (
	"crypto/des"
	"crypto/rand"
	"crypto/rsa"
	"crypto/x509"
	"encoding/base64"
	"encoding/json"
	"encoding/pem"
	"fmt"
	"net/http/httptest"
	"net/url"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/gin-gonic/gin"
	"gucooing/lolo/config"
	"gucooing/lolo/db"
	"gucooing/lolo/pkg/alg"
	"gucooing/lolo/pkg/flyrsa"
	"gucooing/lolo/protocol/quick"
)

type testReply struct {
	Result bool `json:"result"`
	Error  struct {
		Message string `json:"message"`
	} `json:"error"`
	Data json.RawMessage `json:"data"`
}

func sdkCall(t *testing.T, s *Server, version int, method string, body interface{}, desMode bool) testReply {
	t.Helper()
	plain, err := json.Marshal(body)
	if err != nil {
		t.Fatal(err)
	}
	form := url.Values{}
	if desMode {
		block, _ := des.NewCipher([]byte("78143304"))
		padded := alg.PKCS7Padding(plain, block.BlockSize())
		cipher := make([]byte, len(padded))
		for i := 0; i < len(padded); i += block.BlockSize() {
			block.Encrypt(cipher[i:i+block.BlockSize()], padded[i:i+block.BlockSize()])
		}
		form.Set("json_data", base64.StdEncoding.EncodeToString(cipher))
	} else {
		cipher, err := alg.AESECB128Encode([]byte("0b2a18e45d7df321"), plain)
		if err != nil {
			t.Fatal(err)
		}
		form.Set("data", base64.RawStdEncoding.EncodeToString(cipher))
		form.Set("sign", alg.SingBytes(plain, []byte("0b2a18e45d7df321")))
	}
	req := httptest.NewRequest("POST", fmt.Sprintf("/v%d/%s", version, method), strings.NewReader(form.Encode()))
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	w := httptest.NewRecorder()
	s.router.ServeHTTP(w, req)
	var reply testReply
	if err := json.Unmarshal(w.Body.Bytes(), &reply); err != nil {
		t.Fatalf("%s HTTP %d response decode: %v", method, w.Code, err)
	}
	return reply
}

func assertReply(t *testing.T, r testReply, want bool, message string) {
	t.Helper()
	if r.Result != want || (!want && !strings.Contains(r.Error.Message, message)) {
		t.Fatalf("result=%v error=%q; wanted result=%v error containing %q", r.Result, r.Error.Message, want, message)
	}
}
func loginTokens(t *testing.T, r testReply, v int) (string, string, string) {
	t.Helper()
	assertReply(t, r, true, "")
	if v == 1 {
		var d quick.LoginResultV1
		if err := json.Unmarshal(r.Data, &d); err != nil {
			t.Fatal(err)
		}
		return d.AuthToken, d.UserData.Token, d.UserData.Uid
	}
	var d quick.LoginResultV2
	if err := json.Unmarshal(r.Data, &d); err != nil {
		t.Fatal(err)
	}
	return d.AuthToken, d.UserData.Token, d.UserData.Uid
}
func testServer(t *testing.T) *Server {
	t.Helper()
	key, err := rsa.GenerateKey(rand.Reader, 1024)
	if err != nil {
		t.Fatal(err)
	}
	priv, err := flyrsa.NewPrivateKey(pem.EncodeToMemory(&pem.Block{Type: "RSA PRIVATE KEY", Bytes: x509.MarshalPKCS1PrivateKey(key)}))
	if err != nil {
		t.Fatal(err)
	}
	s := &Server{router: gin.New(), rsa: priv}
	s.Router()
	return s
}

func TestSDKAccountAndPersistentLogin(t *testing.T) {
	gin.SetMode(gin.TestMode)
	previous := config.CONF
	cfg := *config.DefaultConfig
	cfg.GucooingApiKey = "isolated-sdk-key"
	config.CONF = &cfg
	defer func() { config.CONF = previous }()
	path := filepath.Join(t.TempDir(), "sdk.sqlite")
	open := func() {
		t.Helper()
		if err := db.NewDB(&db.Option{Type: db.Sqlite, Dsn: path, MaxIdleConns: 1, MaxOpenConns: 1, PersistShardNum: 1}); err != nil {
			t.Fatal(err)
		}
	}
	open()
	t.Cleanup(func() {
		if err := db.Close(); err != nil {
			t.Error(err)
		}
	})
	for i, uid := range []uint32{1, 42, 2147483647} {
		if _, err := db.CreateOFUser(&db.OFUser{UserId: uint32(1000042 + i), SdkUid: uid}); err != nil {
			t.Fatal(err)
		}
	}
	s := testServer(t)
	for _, v := range []int{1, 2} {
		t.Run(fmt.Sprintf("V%d", v), func(t *testing.T) {
			name := fmt.Sprintf("synthetic-v%d", v)
			login := func(password string) testReply {
				return sdkCall(t, s, v, "user/loginByName", gin.H{"username": name, "password": password}, false)
			}
			auth, session, uid := loginTokens(t, login("synthetic-password"), v)
			if uid == "42" || uid == "1" {
				t.Fatal("new SDK ID collided with retained game identity")
			}
			before, err := db.GetOFQuickByAuthToken(auth)
			if err != nil {
				t.Fatal(err)
			}
			assertReply(t, login("wrong"), false, "密码错误")
			assertReply(t, login(""), false, "请输入密码")
			after, err := db.GetOFQuick(before.ID)
			if err != nil {
				t.Fatal(err)
			}
			if before.Password != after.Password || auth != after.AuthToken || session != after.UserToken {
				t.Fatal("failed login changed saved account state")
			}
			auth2, _, uid2 := loginTokens(t, login("synthetic-password"), v)
			if uid2 != uid || auth2 == auth {
				t.Fatal("password login did not refresh stable account")
			}
			assertReply(t, sdkCall(t, s, v, "user/autoLogin", gin.H{"auth_token": auth}, false), false, "失效")
			auth3, _, _ := loginTokens(t, sdkCall(t, s, v, "user/autoLogin", gin.H{"auth_token": auth2}, false), v)
			if auth3 == auth2 {
				t.Fatal("auto login did not rotate auth token")
			}
			// JSON preserves Unicode snowman token bytes in SQLite and on the encrypted wire.
			saved, err := db.GetOFQuickByAuthToken(auth3)
			if err != nil || saved.AuthToken != auth3 {
				t.Fatal("JSON/SQL token roundtrip failed")
			}
			if err := db.Close(); err != nil {
				t.Fatal(err)
			}
			open()
			restarted := &Server{router: gin.New(), rsa: s.rsa}
			restarted.Router()
			s = restarted
			auth3, _, _ = loginTokens(t, sdkCall(t, s, v, "user/autoLogin", gin.H{"auth_token": auth3}, false), v)
			s = testServer(t)
			cfg.GucooingApiKey += "-restart"
			if _, err := s.ToToken(auth3); err == nil {
				t.Fatal("historical token unexpectedly decoded after RSA/key replacement")
			}
			auth4, session4, uid4 := loginTokens(t, sdkCall(t, s, v, "user/autoLogin", gin.H{"authToken": auth3}, false), v)
			if uid4 != uid || auth4 == auth3 {
				t.Fatal("restart lost account or failed to refresh")
			}
			assertReply(t, sdkCall(t, s, v, "user/autoLogin", gin.H{"auth_token": auth3}, false), false, "失效")
			assertReply(t, sdkCall(t, s, 2, "users/checkLogin", gin.H{"uid": "42", "token": session4}, false), false, "不匹配")
			assertReply(t, sdkCall(t, s, 2, "users/checkLogin", gin.H{"uid": uid, "token": session4}, false), true, "")
			// A parseable token with the right ID, but never saved, is not a credential.
			assertReply(t, sdkCall(t, s, v, "user/autoLogin", gin.H{"auth_token": s.GenToken(saved.ID ^ authXor)}, false), false, "失效")
			assertReply(t, sdkCall(t, s, 2, "users/checkLogin", gin.H{"uid": uid, "token": s.GenToken(saved.ID)}, false), false, "不匹配")
			for _, body := range []gin.H{{}, {"username": " ", "password": "x"}, {"username": "missing-password"}} {
				assertReply(t, sdkCall(t, s, v, "user/loginByName", body, false), false, "请输入")
			}
			assertReply(t, sdkCall(t, s, v, "user/autoLogin", gin.H{"auth_token": ""}, false), false, "失效")
			assertReply(t, sdkCall(t, s, v, "user/autoLogin", gin.H{"auth_token": auth4, "authToken": "other"}, false), false, "请求无效")
			// Historical opaque credentials may only recover through exact saved equality.
			saved.AuthToken = "synthetic-legacy-opaque-token"
			if err := db.UpOFQuick(saved); err != nil {
				t.Fatal(err)
			}
			loginTokens(t, sdkCall(t, s, v, "user/autoLogin", gin.H{"auth_token": saved.AuthToken}, v == 2), v)
			assertReply(t, sdkCall(t, s, v, "user/autoLogin", gin.H{"auth_token": saved.AuthToken + "x"}, false), false, "失效")
		})
	}
	// Both IDs exist: a valid session from one account cannot authorize the other.
	a, err := s.passwordLogin("cross-a", "synthetic-password")
	if err != nil {
		t.Fatal(err)
	}
	b, err := s.passwordLogin("cross-b", "synthetic-password")
	if err != nil {
		t.Fatal(err)
	}
	assertReply(t, sdkCall(t, s, 2, "users/checkLogin", gin.H{"uid": fmt.Sprint(b.ID), "token": a.UserToken}, false), false, "不匹配")
	assertReply(t, sdkCall(t, s, 2, "users/checkLogin", gin.H{"uid": "", "token": a.UserToken}, false), false, "不匹配")
	// Token.Time has historically been advisory; preserve this client compatibility.
	payload, _ := json.Marshal(&Token{ID: 77, Time: time.Now().Add(-24 * time.Hour).Unix(), Key: cfg.GucooingApiKey})
	plainServer := &Server{}
	if _, err := plainServer.ToToken(逆天转换(payload)); err != nil {
		t.Fatalf("legacy token time compatibility: %v", err)
	}
	for _, v := range []int{1, 2} {
		r := sdkCall(t, s, v, "system/init", gin.H{}, false)
		assertReply(t, r, true, "")
		if !strings.Contains(string(r.Data), `"mainLoginType":"3"`) || !strings.Contains(string(r.Data), `"rmAccountLg":"0"`) {
			t.Fatal("account login configuration missing")
		}
	}
}
