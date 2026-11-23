# HomeGeometryVerification

独立 .NET 6 控制台验证，直接链接生产 Layout.cs / Geometry.cs，不依赖 Unity 游戏运行时。

```powershell
dotnet run --project tools/HomeGeometryVerification/HomeGeometryVerification.csproj -c Release -p:UseSharedCompilation=false
```

Generate(shape,count,spacing,radius,origin) 支持 24 种 shape：

| shape | 采样及尺寸 |
|---|---|
| line | 沿 X 居中，spacing 为间距 |
| circle | XZ 圆周，radius 为半径 |
| sphere | 球面 Fibonacci 分布，radius 为半径 |
| grid | XZ 近方形网格，spacing 为间距 |
| cube | 边长 2radius 的立方体表面，六面样本数最多相差 1，各面内部低差异采样 |
| disk | XZ 圆盘，按面积均匀的黄金角采样 |
| ball | 实心球，低差异方向与立方根半径分位采样 |
| cylinder | 半径 radius、高 2radius 的 Y 轴圆柱侧面 |
| cone | 底半径 radius、高 2radius 的 Y 轴圆锥侧面，尖端在 +Y，按侧面积采样 |
| helix | Y 轴螺旋，半径 radius、每圈 12 点，高度 spacing*(count-1)；spacing=0 时使用一圈互异圆周点 |
| spiral | XZ 三圈阿基米德螺线，半径从 0 增长到 radius，以弧长近似等距采样 |
| triangle | 正三角形周长，外接圆半径 radius |
| square | 正方形周长，外接圆半径 radius |
| pentagon | 正五边形周长，外接圆半径 radius |
| hexagon | 正六边形周长，外接圆半径 radius |
| star | 五角星凹多边形周长，外半径 radius，内半径为 radius*(3-sqrt(5))/2 |
| ellipse | XZ 椭圆，X/Z 半轴为 radius/radius*.5，等参数角采样 |
| rectangle | XZ 长方形，长宽 2radius/radius，按实际周长 6radius 等距采样 |
| octagon | 正八边形周长，外接圆半径 radius |
| torus | XZ 圆环体表面，主半径 radius、小半径 radius*.3，低差异参数采样 |
| capsule | Y 轴胶囊表面，半径 radius、总高度 4radius；圆柱高 2radius，连两端半球，按各区域面积权重采样 |
| doublehelix | 两支交错存放，相位差 PI，各支每圈 12 点；各支高度 spacing*(各支点数-1) 居中，spacing=0 时各支一圈 |
| heart | XZ 经典参数心形 (16sin³t,13cost-5cos2t-2cos3t-cos4t)/17*radius；4096 段弧长表近似等距采样 |
| wave | X 方向居中，spacing 为 X 间距；Z=radius*sin(i*PI/6) |

所有点先在原点的局部坐标系生成，再按 Unity Z→X→Y 欧拉方向旋转并平移。count 为 1～10000；spacing/radius 必须有限、非负且不超过 100000。零尺寸形状自然退化为重合点。闭合周长不附加重复尾点。Generate 保持现有浮点坐标精度，由上层 Placement 校验进行两位小数保存。

FaceOrigin(Placement item, EditorOrigin origin) 按世界坐标差求家具本地 +Z 朝向，不叠加原点旋转。yaw=atan2(dx,dz)，pitch=-atan2(dy,horizontal)，roll=0。horizontal≤1e-6 时 yaw 使用 origin.Yaw；位置距离≤1e-6 时取 origin 完整三轴。先严格验证原始输入再返回 Rounded 的 Placement，保留家具 ID、配置、位置与缩放（仅做既有两位规范化）。

OrientPattern(string shape,int index,int count,Placement item,EditorOrigin origin) 严格验证 shape、index（0≤index<count）、count（1～10000）、原始家具和原点，仅支持表中 24 种形状。

- sphere/ball 沿用 FaceOrigin 的三维朝向与重合完整姿态回退。
- 其他形状强制 Pitch=Roll=0，仅绕世界 Y 轴旋转；普通形状使用世界原点减家具位置的 XZ 投影。
- triangle/square/pentagon/hexagon/octagon/star 使用所在边的局部向内法线。rectangle 按实际边长定位所属边。采样与朝向共用边索引逻辑，顶点归属从该顶点开始的下一条边，同一条边上的家具保持一致朝向。
- cube 使用 index%6 对应面的法线，依次为 -X、+X、-Y、+Y、-Z、+Z。同一面保持一致朝向，不逐件指向中心。
- 边/面法线先随原点的 Unity Z→X→Y 旋转，再投影到世界 XZ 平面。水平长度≤1e-6 时 Yaw 回退到 origin.Yaw。原点无倾斜时 cube 上下面的法线竖直，故回退到原点 Yaw；原点倾斜后若投影非零则采用该投影，家具仍保持直立。
- 返回 Rounded 家具，保留身份、位置与缩放。FaceOrigin API 本身兼容原有行为。半径或间距为零允许图形自然退化；双螺旋两支在零间距时可能重合。

最新运行：259553 项检查全部通过（仅 .NET 6 既有 NETSDK1138 提示）。新独立参考测试按轴旋转构造预期值，覆盖 24 种朝向、非球直立、边法线垂直且向内、顶点归属、同边/同面一致、旋转后投影、竖直回退、球类兼容和非法索引；新增 8 种形状覆盖 count=1/2/7/101/10000、独立原点旋转与表面方程。

保留原 11596 项测试，新增验证覆盖全部形状在 count=1/2/3/5/6/7/17/101/10000 下的数量与互异性、全部原点旋转、边界与无效参数、立方体表面及六面均衡、圆盘/实心球体积、圆柱/圆锥侧面约束、螺旋高度和螺线弧长、各多边形周长插值，及 500 组世界朝向、六轴、极点、重合阈值、舍入和非法输入。
