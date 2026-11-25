package model

import (
	"encoding/json"
	"gucooing/lolo/gdconf"
	"gucooing/lolo/protocol/excel"
	"gucooing/lolo/protocol/proto"
	"os"
	"reflect"
	"testing"
)

func TestSandboxStoryCoverageAndReadOnly(t *testing.T) {
	loadGiftTestConfig()
	p := new(Player)
	before, _ := json.Marshal(p)
	detail := p.GetQuestDetail()
	byID := make(map[uint32]*proto.Quest)
	main, extra := 0, 0
	for i, q := range detail.Quests {
		if i > 0 && detail.Quests[i-1].QuestId >= q.QuestId {
			t.Fatal("unsorted/duplicate quests")
		}
		if q.Status != proto.QuestStatus_QuestStatus_Finish || q.CompleteCount != 1 || q.BonusTimes != 1 {
			t.Fatal("incomplete quest")
		}
		byID[q.QuestId] = q
		conf := gdconf.GetQuestInfos()[q.QuestId]
		switch proto.EQuestType(conf.Config.NewType) {
		case proto.EQuestType_EQuestType_MainQuest:
			main++
		case proto.EQuestType_EQuestType_ExtraQuest:
			extra++
		default:
			t.Fatal("non-story quest auto-completed")
		}
		for j, c := range q.Conditions {
			if j > 0 && q.Conditions[j-1].ConditionId >= c.ConditionId {
				t.Fatal("unsorted/duplicate condition")
			}
			expected := uint32(1)
			if a := gdconf.GetAchieveConfigure(c.ConditionId); a != nil && a.CountParam > 0 {
				expected = uint32(a.CountParam)
			}
			if c.Status != proto.QuestStatus_QuestStatus_Finish || c.Progress != expected {
				t.Fatal("wrong condition progress")
			}
		}
	}
	if main != 648 || extra != 103 {
		t.Fatalf("review changed resource snapshot: main=%d extra=%d", main, extra)
	}
	raw, err := os.ReadFile("../../Resource/Excel/Story.json")
	if err != nil {
		t.Fatal(err)
	}
	var stories excel.AllStoryDatas
	if err = json.Unmarshal(raw, &stories); err != nil {
		t.Fatal(err)
	}
	check := func(id int32) {
		if byID[uint32(id)] == nil {
			t.Fatalf("missing story prerequisite %d", id)
		}
	}
	for _, story := range stories.GetStory().GetDatas() {
		check(story.QuestID)
	}
	count := 0
	for _, chapter := range stories.GetExtraChapter().GetDatas() {
		for _, story := range chapter.ExtraQuest {
			check(story.QuestID)
			if story.QuestUnlock > 0 {
				check(story.QuestUnlock)
			}
			count++
		}
	}
	if count != 24 {
		t.Fatal("extra story snapshot changed")
	}
	for id, c := range gdconf.GetQuestInfos() {
		if c.Config.NewType != 1 && c.Config.NewType != 9 && SandboxStoryQuest(id) != nil {
			t.Fatal("non-story accepted")
		}
	}
	if SandboxStoryQuest(0) != nil || SandboxStoryQuest(^uint32(0)) != nil {
		t.Fatal("invalid quest accepted")
	}
	if !reflect.DeepEqual(detail, p.GetQuestDetail()) {
		t.Fatal("nondeterministic replay state")
	}
	after, _ := json.Marshal(p)
	if string(before) != string(after) {
		t.Fatal("story projection mutated player")
	}
	q := SandboxStoryQuest(detail.Quests[0].QuestId)
	q.Status = proto.QuestStatus_QuestStatus_InProgress
	if SandboxStoryQuest(q.QuestId).Status != proto.QuestStatus_QuestStatus_Finish {
		t.Fatal("projection alias")
	}
	t.Logf("%d main + %d extra quests; %d story entries + %d extra entries covered", main, extra, len(stories.GetStory().GetDatas()), count)
}
