package game

import (
	"encoding/json"
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

func TestLocalLifeCraftRoutes(t *testing.T) {
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
	for _, kind := range []proto.LifeType{1, 3, 4, 5} {
		id := gdconf.GetLifeCraftRecipeIDs(kind)[0]
		var code uint32
		var req pb.Message
		switch kind {
		case 1:
			code = cmd.CookingFoodReq
			req = &proto.CookingFoodReq{FoodId: id}
		case 3:
			code = cmd.MakePropReq
			req = &proto.MakePropReq{MakeId: id}
		case 4:
			code = cmd.HandicraftReq
			req = &proto.HandicraftReq{HandicraftId: id}
		case 5:
			code = cmd.SewingReq
			req = &proto.SewingReq{SewingId: id}
		}
		for count := int64(1); count <= 2; count++ {
			route(code, req)
			if len(c.bodies) != 2 || c.packets[0] != 0 || c.packets[1] != 71 {
				t.Fatalf("kind %v packet sequence: %v", kind, c.bodies)
			}
			pack, ok := c.bodies[0].(*proto.PackNotice)
			if !ok || pack.Status != proto.StatusCode_StatusCode_OK || len(pack.Items) != 1 {
				t.Fatal("missing pack")
			}
			var result *proto.LifeCommonResult
			var status proto.StatusCode
			switch r := c.bodies[1].(type) {
			case *proto.CookingFoodRsp:
				result = r.LifeCommonResult
				status = r.Status
			case *proto.MakePropRsp:
				result = r.LifeCommonResult
				status = r.Status
			case *proto.HandicraftRsp:
				result = r.LifeCommonResult
				status = r.Status
			case *proto.SewingRsp:
				result = r.LifeCommonResult
				status = r.Status
			}
			if status != proto.StatusCode_StatusCode_OK || !result.GetIsSuccess() || result.LifeType != kind || len(result.ResultItems) != 1 || p.Life.Crafted[kind][id] != uint32(count) {
				t.Fatal("incomplete manufacturing transaction")
			}
			if got := result.ResultItems[0].GetMainItem().GetBaseItem(); got != nil {
				if got.Num != 1 || pack.Items[0].GetMainItem().GetBaseItem().Num != count {
					t.Fatal("delta/absolute mismatch")
				}
			}
		}
		route(cmd.GetLifeInfoReq, &proto.GetLifeInfoReq{LifeType: kind})
		info := c.bodies[0].(*proto.GetLifeInfoRsp)
		if len(c.bodies) != 1 || c.packets[0] != 71 || info.Status != proto.StatusCode_StatusCode_OK || info.LifeBaseInfo.Level != gdconf.GetLifeCraftKind(kind).MaxLevel || len(info.LifeSkill.LifeSkillInfo) == 0 {
			t.Fatal("life entry unavailable")
		}
		skill := info.LifeSkill.LifeSkillInfo[0].SkillId
		route(cmd.LifeSkillLevelUpReq, &proto.LifeSkillLevelUpReq{LifeType: kind, SkillId: skill, IsForceStudy: true})
		up := c.bodies[0].(*proto.LifeSkillLevelUpRsp)
		if c.packets[0] != 71 || up.Status != proto.StatusCode_StatusCode_OK || !up.IsForceStudy || len(up.LifeSkills.LifeSkillInfo) == 0 {
			t.Fatal("skill entry unavailable")
		}
		route(cmd.GetOneTypeLifePictorialBookCountReq, &proto.GetOneTypeLifePictorialBookCountReq{LifeType: kind})
		if len(c.bodies) != 1 || c.packets[0] != 71 || c.bodies[0].(*proto.GetOneTypeLifePictorialBookCountRsp).Status != proto.StatusCode_StatusCode_OK {
			t.Fatal("pictorial query unavailable")
		}
	}
	before, _ := json.Marshal(p)
	route(cmd.MakePropReq, &proto.MakePropReq{MakeId: 0})
	after, _ := json.Marshal(p)
	if len(c.bodies) != 1 || c.packets[0] != 71 || c.bodies[0].(*proto.MakePropRsp).Status != proto.StatusCode_StatusCode_BAD_REQ || string(before) != string(after) {
		t.Fatal("invalid recipe mutated state")
	}
	config.GetGame().Sandbox = false
	before, _ = json.Marshal(p)
	route(cmd.MakePropReq, &proto.MakePropReq{MakeId: gdconf.GetLifeCraftRecipeIDs(3)[0]})
	after, _ = json.Marshal(p)
	if len(c.bodies) != 1 || c.bodies[0].(*proto.MakePropRsp).Status != proto.StatusCode_StatusCode_BAD_REQ || string(before) != string(after) {
		t.Fatal("strict mode mutated state")
	}
}
