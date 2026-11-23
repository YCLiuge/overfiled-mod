$ErrorActionPreference='Stop'
function Replace-Literal($path,$old,$new) {
 $absolute=Join-Path (Get-Location) $path
 $text=[IO.File]::ReadAllText($absolute).Replace("`r`n","`n")
 $old=$old.Replace("`r`n","`n"); $new=$new.Replace("`r`n","`n")
 if($text.Contains($new)){return}
 if(-not $text.Contains($old)){throw "Missing expected text in $path : $old"}
 [IO.File]::WriteAllText($absolute,$text.Replace($old,$new),[Text.UTF8Encoding]::new($false))
}
Replace-Literal 'DrawGuessModule/ChannelSync.cs' '        _client.GameMessageReceived -= _messageHandler;' "        _client.BoardMessageReceived -= _boardHandler;`n        _client.GameMessageReceived -= _messageHandler;"
Replace-Literal 'DrawGuessModule/ChannelSync.cs' '                shared.GameMessageReceived += _messageHandler;' "                AttachBoards(shared, generation);`n                shared.GameMessageReceived += _messageHandler;"
Replace-Literal 'DrawGuessModule/ChannelSync.cs' '        bool hadRoom = _room != null || _requestedRoom != null;' "        ResetBoards();`n        bool hadRoom = _room != null || _requestedRoom != null;"
Replace-Literal 'DrawGuessModule/MainLogic.cs' '        UpdatePanel();' "        TickBoards();`n        UpdatePanel();"
Replace-Literal 'DrawGuessModule/MainLogic.cs' '        "invalid_guess" => "答案需为 1–64 字符",' "        `"invalid_word`" => `"题目须为 1–16 字且不能包含控制字符`",`n        `"invalid_guess`" => `"答案需为 1–64 字符`","
$old=@'
                if (_room != null && !IsHost) throw new InvalidOperationException("只有房主可以移动房间画架");
                PaintingSystem.PlaceSceneBoard(); PublishState(true); Notice("场景画架已放到玩家前方，Alt＋点击画架打开面板"); return;
'@
Replace-Literal 'DrawGuessModule/MainLogic.cs' $old '                CreateOwnBoard(); return;'
Replace-Literal 'DrawGuessModule/MainLogic.cs' '            if (type == "status")' '            if (type == "boardselect" || type == "boardjoin") { if (p.Length < 3 || p.Length > 4) throw new ArgumentException("请点击场景画架"); SelectBoard(p[2], p.Length == 4 ? p[3] : null); return; }
            if (type == "status")'
Replace-Literal 'DrawGuessModule/MainLogic.cs' '                case "guess":' '                case "word": case "guess":'
Replace-Literal 'DrawGuessModule/MainLogic.cs' 'input.Type != "next" && input.Type != "guess"' 'input.Type != "next" && input.Type != "word" && input.Type != "guess"'
Replace-Literal 'DrawGuessModule/MainLogic.cs' '        if (IsFree && _room == null) { SubmitOffline(input); return; }' '        if (IsFree && _room == null) { if (_selectedBoard.Length > 0 && _selectedBoard != _client?.SessionId) { Notice("普通画架仅创建者可以绘画"); return; } SubmitOffline(input); return; }'
Replace-Literal 'DrawGuessModule/MainLogic.cs' '        if (IsFree) { UpdateFreePanel(); return; }' '        if (IsFree) { PaintingSystem.SetTopicState("", false); UpdateFreePanel(); return; }
        PaintingSystem.SetTopicState(_view?.Phase ?? "", _view?.DrawerId == _client?.SessionId && _view != null);'
Replace-Literal 'DrawGuessModule/MainLogic.cs' '"lobby" => "大厅", "playing"' '"lobby" => "大厅", "choosing" => "画手出题（30秒内）", "playing"'
Replace-Literal 'DrawGuessModule/MainLogic.cs' '            else if (view.Phase == "playing" && !draw)' '            else if (view.LastEvent == "word_timeout") detail += "\n画手未出题，本轮跳过";
            else if (view.Phase == "playing" && !draw)'
Replace-Literal 'DrawGuessModule/MainLogic.cs' '        PaintingSystem.Dispose(); _clock.Stop();' '        ResetBoards(); PaintingSystem.Dispose(); _clock.Stop();'
Replace-Literal 'DrawGuessModule/PaintingSystem.cs' '            RemoveSceneBoard();' '            RemoveSceneBoard();
            ClearChannelBoards();'
Replace-Literal 'DrawGuessModule/FreePaintingRuntime.cs' '连接后创建或加入独立自由画室' '连接后创建自己的频道画架，别人只能观看'
Replace-Literal 'DrawGuessModule/MainLogic.cs' '        _busy = true; int generation = _generation;' '        if (IsFree) throw new InvalidOperationException("普通画架自动同步到频道，无需自由画室；请切换你画我猜再创建游戏房间");
        ChannelBoard? binding = host ? _ownBoard : _boards.Values.FirstOrDefault(b => b.Room == room && b.RoomInstance.Length > 0 && (expectedHost == null || b.Name == expectedHost));
        if (binding == null) throw new InvalidOperationException(host ? "请先放置自己创建的画架，再创建游戏房间" : "请点击房主已绑定游戏房间的画架加入");
        string boundOwner = binding.Owner, boundInstance = binding.RoomInstance;
        _busy = true; int generation = _generation;'
Replace-Literal 'DrawGuessModule/MainLogic.cs' '                if (!host && expectedHost != null && joined.HostNick != expectedHost)' '                if (!host && (joined.HostId != boundOwner || joined.InstanceId != boundInstance || (expectedHost != null && joined.HostNick != expectedHost)))'
Replace-Literal 'DrawGuessModule/MainLogic.cs' '                    PaintingSystem.Show(); Notice(host ? "房间已创建，等待玩家加入" : "已加入，等待房主同步");' '                    _boardDirty = true; _lastBoardInfo = -10; TickBoards();
                    PaintingSystem.Show(); Notice(host ? "房间已绑定自己的游戏画架，其他玩家点击此画架加入" : "已加入，等待房主同步");'
