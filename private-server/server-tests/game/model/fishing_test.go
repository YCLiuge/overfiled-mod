package model

import (
	"encoding/json"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/protocol/proto"
	"math"
	"testing"
	"time"
)

func fishingTestPlayer(t *testing.T) (*Player, *proto.FishingReq) {
	t.Helper()
	loadGiftTestConfig()
	for id, c := range gdconf.GetWeaponAllMap() {
		if c != nil && c.WeaponInfo != nil && c.WeaponInfo.NewWeaponType == 18 {
			p := &Player{Item: &ItemModel{ItemWeaponMap: map[uint32]*ItemWeaponInfo{7: {WeaponId: id, InstanceId: 7}}, ItemBaseInfo: map[uint32]*ItemBaseInfo{}}}
			return p, &proto.FishingReq{FishingRodId: 7, FishingPondId: 1001}
		}
	}
	t.Fatal("real resource rod missing")
	return nil, nil
}
func TestLocalFishingPlanRewardPersistence(t *testing.T) {
	p, req := fishingTestPlayer(t)
	now := time.Unix(1000, 0)
	plan, err := p.StartFishing(req, 20001, now, true)
	if err != nil {
		t.Fatal(err)
	}
	conf := gdconf.GetFishingConfig()
	fish := plan.FishInfos[0]
	c := conf.Fish[fish.ID]
	if plan.CostTime != 6 || plan.CostEndurance != 0 || fish.Length < c.MinSize || fish.Length > c.MaxSize || len(plan.FishingEventList) == 0 {
		t.Fatalf("bad animation plan %v", plan)
	}
	for _, e := range plan.FishingEventList {
		found := false
		for _, v := range conf.Events[e.ID].GetFishingEventItems() {
			if uint32(v.Index) == e.Index {
				found = true
			}
		}
		if !found {
			t.Fatal("invented event")
		}
	}
	if _, err = p.StartFishing(req, 20001, now, true); err == nil {
		t.Fatal("duplicate cast accepted")
	}
	if r, _ := p.CompleteFishing(now.Add(5*time.Second), 20001); r != nil {
		t.Fatal("early grant")
	}
	raw, _ := json.Marshal(p)
	restored := &Player{}
	if err = json.Unmarshal(raw, restored); err != nil {
		t.Fatal(err)
	}
	if restored.Fishing.Pending != nil {
		t.Fatal("pending cast persisted")
	}
	result, pack := p.CompleteFishing(now.Add(6*time.Second), 20001)
	if result == nil || result.Status != proto.StatusCode_StatusCode_OK || giftCounts(result.Items)[uint32(c.ItemID)] != 1 || giftCounts(pack.Items)[uint32(c.ItemID)] != 1 {
		t.Fatal("reward/pack mismatch")
	}
	if result, _ = p.CompleteFishing(now.Add(time.Hour), 20001); result != nil {
		t.Fatal("duplicate reward")
	}
	raw, _ = json.Marshal(p)
	restored = &Player{}
	json.Unmarshal(raw, restored)
	if restored.Fishing.Total != 1 || restored.Fishing.Catches[fish.ID] != 1 || restored.Item.ItemBaseInfo[uint32(c.ItemID)].Num != 1 || restored.Fishing.BestLength[fish.ID] != fish.Length {
		t.Fatal("catch or inventory not persisted")
	}
	if len(restored.FishingPictorialCounts()) == 0 {
		t.Fatal("real fish achievement join missing")
	}
	if p.LifeBase(2, true).Level != 10 || len(p.LifeSkills(2, true).LifeSkillInfo) != 6 {
		t.Fatal("fishing UI level/skills absent")
	}
}
func TestLocalFishingRejectCancelSceneAndOverflow(t *testing.T) {
	p, req := fishingTestPlayer(t)
	now := time.Unix(1000, 0)
	cases := []*proto.FishingReq{nil, {FishingRodId: 7, FishingPondId: 999}, {FishingRodId: math.MaxUint32, FishingPondId: 1001}, {FishingRodId: 7, FishingPondId: 1001, FishingBoxId: 101}}
	for _, bad := range cases {
		before, _ := json.Marshal(p)
		if _, err := p.StartFishing(bad, 1, now, true); err == nil {
			t.Fatal("bad request accepted")
		}
		after, _ := json.Marshal(p)
		if string(before) != string(after) {
			t.Fatal("bad request mutated player")
		}
	}
	if _, err := p.StartFishing(req, 1, now, false); err == nil {
		t.Fatal("strict mode accepted")
	}
	if _, err := p.StartFishing(req, 1, now, true); err != nil {
		t.Fatal(err)
	}
	p.CancelFishing()
	if result, _ := p.CompleteFishing(now.Add(time.Hour), 1); result != nil {
		t.Fatal("canceled grant")
	}
	p.StartFishing(req, 1, now, true)
	if result, _ := p.CompleteFishing(now.Add(time.Hour), 2); result != nil || p.Fishing.Pending != nil {
		t.Fatal("scene switch grant")
	}
	p.StartFishing(req, 1, now, true)
	cast := p.Fishing.Pending
	p.Item.ItemBaseInfo[cast.ItemID] = &ItemBaseInfo{ItemId: cast.ItemID, Num: math.MaxInt64}
	result, _ := p.CompleteFishing(now.Add(time.Hour), 1)
	if result == nil || result.Status == proto.StatusCode_StatusCode_OK || p.Item.ItemBaseInfo[cast.ItemID].Num != math.MaxInt64 || p.Fishing.Total != 0 {
		t.Fatal("overflow granted")
	}
}
