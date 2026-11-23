using GomokuConsole;
using Il2CppInutan;
using Il2CppKinematicCharacterController;
using MelonLoader;
using mod;
using UnityEngine;
using System.Collections;
namespace mod
{
    //5040006 黑
    //5040015 白
    //5040014 红
    //5040007 蓝
    //30 80 x z
    //0.74
    internal class Game_Qizi5
    {
        public static Game_Qizi5 i = new();
        Game_Qizi5() { if (i == null) i = this; }
        public Game game = new();
        KinematicCharacterMotor motor;
        public int stacount = 0;

        int x = 30;
        int z = 80;
        int nx = 0;
        int nz = 0;
        public void Start()
        {
            motor = TipMsgCore.i.players[PlayerSystem.Instance.PlayerEntities[PlayerSystem.Instance.MainPlayerID].PlayerModel.NickName].root.GetComponent<KinematicCharacterMotor>();
            var a = GameObject.Find("放置网格/PlacementGrid_9999(Clone)/Grid_1");
            a.transform.localScale = new Vector3(100, 1, 100);
            a.transform.position = new Vector3(0, -6, 0);
            for (int i = 0; i < a.transform.parent.childCount; i++)
            {
                if (a.transform.parent.GetChild(i).GetComponent<HomeItem_Furniture>() != null)
                {
                    a.transform.parent.GetChild(i).transform.position = new Vector3(10, -7, 60);
                    a.transform.parent.GetChild(i).transform.rotation = game.quaternion;
                    a.transform.parent.GetChild(i).transform.localScale = new Vector3(100, 0.4f, 100);
                    break;
                }
            }
            stacount = a.transform.parent.childCount;

        }
        public void Updata()
        {

        }
        public int getnum()
        {
            return game.num;
        }
        float time = 0;
        bool set = false;
        bool canput = false;
        public void SetQiPan()
        {

            motor.SetPosition(new Vector3(x + (nx * 1.14f), -6.5f, z + (nz * 1.14f) - 0.74f));
            motor.SetRotation(game.quaternion);

            if (time < 1.5) { time += Time.deltaTime; return; }
            else time = 0;

            if (nz >= 15)
            {
                TipMsgCore.Stute = Stute_tokensys.None;
                TipMsgCore.i.Send("结束");
                nx = 0;
                nz = 0;
            }
            else
            {
                if (canput)
                {
                    canput = false;
                    UIHomeSystem.Instance.Putdown(); 
                    set = !set;
                    nx++;
                    if (nx >= 15) { nz++; nx = 0; }
                }
                else
                {
                    UIHomeSystem.Instance.ChooseItem(set == true ? 5040014 : 5040007);
                    canput = true;
                }
            }
        }

        public IEnumerator ClearQiPan()
        {
            // 1. 查找网格
            var grid = GameObject.Find("放置网格/PlacementGrid_9999(Clone)");
            if (grid == null)
            {
                stc.Log("找不到网格，无法清盘");
                yield break;
            }
            yield return new WaitForSeconds(2f);
            // 2. 先收集所有需要回收的家具组件（避免动态列表变化）
            var furnitureList = new List<HomeItem_Furniture>();
            int childCount = grid.transform.childCount;
            for (int i = stacount; i < childCount; i++)
            {
                var child = grid.transform.GetChild(i);
                var furniture = child.GetComponent<HomeItem_Furniture>();
                if (furniture != null)
                    furnitureList.Add(furniture);
                else
                    stc.Log($"子物体 {i} 没有 HomeItem_Furniture 组件");
            }

            if (furnitureList.Count == 0)
            {
                stc.Log("没有需要回收的家具");
                TipMsgCore.Stute = Stute_tokensys.None;
                yield break;
            }

            // 3. 逐个回收
            foreach (var furniture in furnitureList)
            {
                if (furniture == null) continue;

                long id = furniture.id; // 假设 id 是 long 类型
                if (id == 0)
                {
                    stc.Log("家具 ID 为 0，跳过");
                    continue;
                }

                // 从字典中获取 HomeItem
                if (IHomeSystem.sys.allFurnitures.TryGetValue(id, out HomeItem_Furniture homeItem))
                {
                    stc.Log($"正在回收家具 ID: {id}");
                    IHomeSystem.sys.TakeBackConfirm(homeItem, true);

                    // 等待回收完成（需要根据实际情况调整）
                    // 如果 TakeBackConfirm 有异步回调，建议用事件或协程等待
                    yield return new WaitForSeconds(0.5f); // 短暂延迟，避免过于密集
                }
                else
                {
                    stc.Log($"字典中找不到 ID: {id} 的 HomeItem");
                }
            }

            // 4. 更新状态
            Game_Qizi5.i.game.reset();
            TipMsgCore.Stute = Stute_tokensys.None;
            stc.Log("清盘完成");
        }
    }
}

namespace GomokuConsole
{
    // 棋子颜色
    enum Player { None, Black, White }

    // 棋盘类：管理15x15网格和核心逻辑
    class Board
    {
        public const int Size = 15;
        private Player[,] grid;

        public Board()
        {
            grid = new Player[Size, Size];
        }

        // 落子：成功返回 true
        public bool PlaceStone(int row, int col, Player player)
        {
            if (row < 0 || row >= Size || col < 0 || col >= Size)
                return false;
            if (grid[row, col] != Player.None)
                return false;
            grid[row, col] = player;
            return true;
        }

        public Player GetStone(int row, int col) => grid[row, col];

        // 检查棋盘是否已满（平局）
        public bool IsFull()
        {
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                    if (grid[r, c] == Player.None)
                        return false;
            return true;
        }

        // 判断以 (row, col) 为中心的四个方向是否有五子连珠
        public bool CheckWin(int row, int col, Player player)
        {
            // 四个方向向量：水平、垂直、主对角线、副对角线
            int[][] directions = new int[][]
            {
                new int[]{ 1, 0 },
                new int[]{ 0, 1 },
                new int[]{ 1, 1 },
                new int[]{ 1, -1 }
            };

            foreach (var dir in directions)
            {
                int count = 1;

                // 正方向延伸
                for (int step = 1; step < 5; step++)
                {
                    int nr = row + dir[0] * step;
                    int nc = col + dir[1] * step;
                    if (nr < 0 || nr >= Size || nc < 0 || nc >= Size) break;
                    if (grid[nr, nc] == player) count++;
                    else break;
                }

                // 反方向延伸
                for (int step = 1; step < 5; step++)
                {
                    int nr = row - dir[0] * step;
                    int nc = col - dir[1] * step;
                    if (nr < 0 || nr >= Size || nc < 0 || nc >= Size) break;
                    if (grid[nr, nc] == player) count++;
                    else break;
                }

                if (count >= 5) return true;
            }
            return false;
        }

    }

    // 游戏流程控制
    class Game
    {
        private Board board = new Board();
        private Player currentPlayer = Player.Black;
        string playerBlack;
        string oplayerBlack;
        public int num = 0;
        public KinematicCharacterMotor mainplayer;
        public Quaternion quaternion = new(0, 0, 0, 0);
        int X;
        int Z;
        Transform root;
        public void Start()
        {
            //tokensys.i.Send("===== 五子棋游戏（双人对战） =====");
            TipMsgCore.i.Send( "黑方:"+playerBlack + "先走,白方:" + oplayerBlack + "后走");
            mainplayer = TipMsgCore.i.players[PlayerSystem.Instance.PlayerEntities[PlayerSystem.Instance.MainPlayerID].PlayerModel.NickName].root.GetComponent<KinematicCharacterMotor>();
            
            var a = GameObject.Find("放置网格/PlacementGrid_9999(Clone)/Grid_1");
            a.transform.localScale = new Vector3(100, 1, 100);
            a.transform.position = new Vector3(0, -6, 0);
            root = a.transform.parent;
           Game_Qizi5.i.stacount = a.transform.parent.childCount;
            num = 0;
        }
        public void Setqizi(string name, string msg)
        {
            //if (msg != "0" || name != playerBlack || name != oplayerBlack) return;


            if (msg == "over" && (name == playerBlack || name == oplayerBlack)) gameover();
            if (msg != "0") return;
            if ((currentPlayer == Player.Black && name == playerBlack) || (currentPlayer == Player.White && name == oplayerBlack)) { }
            else
            {
                TipMsgCore.i.Send("该对方下");
                return;
            }
            X = (int)((TipMsgCore.i.players[name].root.position.x - 30 + 0.57f) / 1.14);
            Z = (int)((TipMsgCore.i.players[name].root.position.z - 80 + 0.57f) / 1.14);
            string input = X + " " + Z; 

            TipMsgCore.i.Send(name+"坐标："+input);
            

            // 解析输入
            string[] parts = input?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts == null || parts.Length != 2)
            {
                stc.Log("输入格式错误，请重新输入。\n");
                return;
            }
            if (!int.TryParse(parts[0], out int row) || !int.TryParse(parts[1], out int col))
            {
                stc.Log("请输入有效的整数。\n");
                return;
            }

            // 尝试落子
            if (!board.PlaceStone(row, col, currentPlayer))
            {
                TipMsgCore.i.Send("该位置无效或已被占用，请重新选择。");
                return;
            }

            // 判断胜负
            if (board.CheckWin(row, col, currentPlayer))
            {

                TipMsgCore.i.Send($" 获胜者：{(currentPlayer == Player.Black ? playerBlack : oplayerBlack)}");
                gameover();
            }

            // 判断平局
            if (board.IsFull())
            {

                TipMsgCore.i.Send("平局！棋盘已满。");
                gameover();
            }


            mainplayer.SetPosition(new Vector3(30 + (X * 1.14f), -5.5f, 80 + (Z * 1.14f) - 0.74f));
            UIHomeSystem.Instance.ChooseItem((currentPlayer == Player.Black) ? 5040006 : 5040015);
            MelonCoroutines.Start(DelayedFunction());
            MelonCoroutines.Start(DelayedFunction1());

            // 切换玩家
            currentPlayer = (currentPlayer == Player.Black) ? Player.White : Player.Black;
            
        }
        private IEnumerator DelayedFunction()
        {
            // 等待 2 秒（真实时间）
            yield return new WaitForSeconds(1f);

           root.GetChild(root.childCount-1).gameObject.SetActive(false);

           UIHomeSystem.Instance.Invoke("Putdown", 1f);
        }
        private IEnumerator DelayedFunction1()
        {
            // 等待 2 秒（真实时间）
            yield return new WaitForSeconds(3f);

            root.GetChild(root.childCount - 1).gameObject.SetActive(true);

        }

        public void Updata()
        {
            mainplayer.SetRotation(quaternion);
            mainplayer.SetPosition(new Vector3(30 + (X * 1.14f), -5.5f, 80 + (Z * 1.14f) - 0.74f));
        }
        public void gameover() {
           
            TipMsgCore.Stute = Stute_tokensys.ClearQiPaning;//Stute.Qizi5end;
            TipMsgCore.i.Send("游戏结束,清理棋盘");
            MelonCoroutines.Start(Game_Qizi5.i.ClearQiPan());
        }
        public void SetPlayer(string name)
        {
            if (num == 0) playerBlack = name;
            if (num == 1) oplayerBlack = name;
            num++;
        }

        public void reset()
        {
            board = new Board();
            num = 0;
            currentPlayer = Player.Black;
        }

     
    }

    // 程序入口
    //class Program
    //{
    //    static void Main(string[] args)
    //    {
    //        Game game = new Game();
    //        game.Start();
    //    }
    //}
}