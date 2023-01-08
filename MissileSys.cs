/*
灰陨——弹体系统程序
请将本程序放置于弹体蓝图的程序块中
*/

bool isInit = false;
// 通信配置
static UpdateType us;
static IMyBroadcastListener _myBroadcastListener;
static string AntennaTag = "Tag";  // 默认通信频道

Ship MisShip;   // 导弹Ship类
int shipTag;    // 导弹标识

Ship.Target LockTarget;
string status = "idle";

long targetId;
Vector3D targetPosition;
int targetStamp = 0;
bool isLaunch = false;
int setupStamp = 0, launchStamp, paulseStamp;

List<IMyWarhead> Warheads;  // 弹头列表
List<IMyShipMergeBlock> Mergeblocks;    // 合并块列表
List<IMyRadioAntenna> Antennas; // 天线列表


class RMsg {    // 自定义通讯类
    // public Vector3D Pos;
    public string instruction, tag, data;
    public int reciveStamp;

    public RMsg(string text) {
        string[] infos = text.Split('#');
        if(infos.Length < 3) return;
        instruction = infos[0];
        tag = infos[1];
        data = infos[2];
        reciveStamp = Ship.timetick;
    }

    public static string recive() {
        if( (us & UpdateType.IGC) >0)
        { 
            while (_myBroadcastListener.HasPendingMessage)
            {
                MyIGCMessage myIGCMessage = _myBroadcastListener.AcceptMessage();
                if(myIGCMessage.Tag == AntennaTag)
                { // This is our tag
                    if(myIGCMessage.Data is string)
                    {
                        string str = myIGCMessage.Data.ToString();
                        return str;
                    }
                }
            }
        }
        return "";
    }

    public void send() {
        if ( true ||    // [*]
            (us & (UpdateType.Trigger | UpdateType.Terminal)) > 0
            || (us & (UpdateType.Mod)) > 0 
            || (us & (UpdateType.Script)) > 0
            )
        { 
            MEA.PG.IGC.SendBroadcastMessage(AntennaTag, instruction + "#" + tag + "#" + data);
        }
    }
}


void init() {   // 初始化函数
    if(isInit) return;
    // 初始化内容

    if(MisShip == null || MisShip.Debug != "Normal"){
        MisShip = newShipWithNameContantBlocks();
    }

    // 获取天线
    Antennas = new List<IMyRadioAntenna>();
    GridTerminalSystem.GetBlocksOfType(Antennas);
    
    // 获取弹头
    Warheads = new List<IMyWarhead>();
    GridTerminalSystem.GetBlocksOfType(Warheads);

    // 导弹初始化完毕，进入发射程序
    
    isInit = true;
}

int teststamp = 0;

void Main(string arguments, UpdateType updateSource)
{
    us = updateSource;  // 更新资源

    Echo("setupStamp: " + setupStamp.ToString() + "  launchStamp: " + launchStamp.ToString());

    if (isLaunch && ((Ship.timetick - launchStamp) > 30)) { // 开始发射后
        init();
        MisShip.UpdatePhysical(); // 更新飞船的物理信息，其他的很多方法都依赖于飞船的物理信息

        // 通信处理
        string recive = RMsg.recive();
        if(recive != "") {
            RMsg rsg = new RMsg(recive);
            if(rsg.instruction == "Report") {
                if(long.Parse(rsg.tag) == targetId) {
                    Vector3D temp = new Vector3D();
                    Vector3D.TryParse(rsg.data, out temp);
                    targetPosition = temp;
                    Echo(targetPosition.ToString());

                    teststamp = Ship.timetick;
                }
            }
        }
        Echo("Last Recived tick: " + teststamp.ToString());

        if (status == "inDock") { // 在起射状态
            // MisShip.SetThrustOverride("All", 0);
            MisShip.SetThrustOverride("Backward", 100);

            foreach(IMyRadioAntenna b in Antennas) {
                b.Radius = 50000;   // 开启所有天线范围到最大
                b.HudText = "发射中";// 设置文本
            }

            foreach(IMyWarhead b in Warheads) {
                b.IsArmed = false;  // 解除所有弹头
            }
            
            if(((Ship.timetick - launchStamp) / 60) > 3) {
                MisShip.SetGyroValue(0, 0, 0);
                MisShip.MotionInit();
                status = "CRUISING";
            }

        } else if(status == "CRUISING") { // 巡航状态
            foreach(IMyRadioAntenna b in Antennas) {
                b.HudText = "巡航中";// 设置文本
            }
            
            if(MisShip.AimAtPosition(targetPosition)) {
                MisShip.SetThrustOverride("Backward", 50);
            } else {
                MisShip.SetThrustOverride("Backward", 0);
            }
            Echo("targetPos: " + targetPosition.ToString());
            
            Ship.Target target = MisShip.ScanPoint(targetPosition);
            if(!target.IsEmpty()){
                if(targetId == target.EntityId) {   // 找到相符合的目标
                    LockTarget = target;
                    if((Ship.timetick - launchStamp) > 300) {
                        status = "GUIDANCE";
                        MisShip.MotionInit();
                        foreach(IMyWarhead b in Warheads) {
                            b.IsArmed = true;  // 装配所有弹头
                        }
                    }
                }
            }
            
            if(((Ship.timetick - launchStamp) / 3600) > 5) {
                foreach(IMyWarhead b in Warheads) {
                    b.Detonate();   // 发射超过五分钟自动引爆
                }
            }

        } else if(status == "GUIDANCE") {// 制导状态
            foreach(IMyRadioAntenna b in Antennas) {
                b.HudText = "制导中";// 设置文本
            }
            
            double hitTime = getDistance() / Vector3D.Distance(MisShip.Velocity, new Vector3D(0, 0, 0) );
            // Vector3D hitPoint = LockTarget.Position;
            Vector3D hitPoint = Ship.HitPointCaculate(MisShip.Position, new Vector3D(0, 0, 0), MisShip.Acceleration, LockTarget.Position, LockTarget.EntityInfo.Velocity, LockTarget.Acceleration, 100, 1, 100);
            // Vector3D hitPoint = Ship.HitPointCaculate(
            //     MisShip.Position, MisShip.Velocity, MisShip.Acceleration,
            //     LockTarget.Position, LockTarget.Velocity, LockTarget.Acceleration,
            //     100, 100, 1
            // );

            if(MisShip.AimAtPosition(hitPoint)) {
                MisShip.SetThrustOverride("Backward", 100);
            } else {
                MisShip.SetThrustOverride("Backward", 0);
            }
            Echo("hitTime = " + hitTime.ToString());
            Echo("hitpos = " + hitPoint.ToString());

            Ship.Target t = MisShip.TrackTarget(LockTarget);
            if(!t.IsEmpty() && t.EntityId == LockTarget.EntityId) { // 找到目标
                LockTarget = t;  // 更新目标信息
                targetStamp = Ship.timetick;    // 记录最后一次汇报时间
            } else {
                if(Ship.timetick - targetStamp > 60) {
                    status = "CRUISING";
                    MisShip.MotionInit();
                }
            }
        }

        if(Ship.timetick - paulseStamp > 60 + shipTag * 5) {
            RMsg m = new RMsg("mis#" + Me.EntityId.ToString() + "#" + status + "&" + getDistance().ToString());
            m.send();   // 发送导弹心跳数据包
            paulseStamp = Ship.timetick;
        }

    } else {    // 开始发射前
        if(!isLaunch) {
            Echo("MyTag: " + shipTag.ToString());
            // 导弹检测机制检测到编程块写入目标id时断开合并块进入发射程序
            if(Me.CustomData != "") {   // 发射前设置
                if(setupStamp == 0) {
                    setupStamp = Ship.timetick;
                }
                RMsg data = new RMsg(Me.CustomData);
                if(data.instruction == "attack") {
                    targetId = long.Parse(data.tag);
                    Vector3D temp = new Vector3D();
                    Vector3D.TryParse(data.data, out temp);
                    targetPosition = temp;
                    
                    // 分离程序
                    if(((Ship.timetick - setupStamp) / 60) > shipTag * 2) {
                        isLaunch = true;
                        status = "inDock";
                        launchStamp = Ship.timetick;
                        Mergeblocks = new List<IMyShipMergeBlock>();
                        GridTerminalSystem.GetBlocksOfType(Mergeblocks, b => b.CustomName.Contains("[Mis-"+shipTag+"]"));
                        foreach(IMyShipMergeBlock b in Mergeblocks) {
                            b.Enabled = false;  // 关闭所有合并块
                        }
                    }
                    // 获取合并块
                        
                }
            }
        }
    }

    Ship.timetick ++ ; //更新Ship类的时间变量
    //在编程块右下角显示帧计数变量
	Echo(status + " | " + Ship.timetick.ToString());
}

// 计算碰撞点
Vector3D HitPointCaculate(Vector3D pos1, Vector3D vel1, Vector3D acc1, Vector3D pos2, Vector3D vel2, Vector3D acc2, double maxVel1, double maxVel2, double maxAcc) {
    // 计算导弹和目标的相对位置、速度和加速度
    Vector3D r = pos2 - pos1;
    Vector3D v = vel2 - vel1;
    Vector3D a = acc2 - acc1;

    // 计算导弹和目标的加速度平方和
    double a2 = a.LengthSquared();

    // 计算导弹和目标的相对位置、速度和加速度的乘积
    double rv = r.Dot(v);
    double ra = r.Dot(a);
    double va = v.Dot(a);

    // 计算导弹和目标的相对位置的平方
    double r2 = r.LengthSquared();

    // 计算导弹和目标的最高速度平方
    double v1_2 = maxVel1 * maxVel1;
    double v2_2 = maxVel2 * maxVel2;

    // 计算导弹和目标的最高加速度平方
    double a1_2 = maxAcc * maxAcc;
    double a2_2 = maxAcc * maxAcc;

    // 计算交汇点
    double t = (v1_2 - v2_2 - a1_2 + a2_2 + 2 * rv) / (2 * a2 - 4 * va);
    Vector3D hitPoint = pos1 + vel1 * t + 0.5 * acc1 * t * t;
    return hitPoint;
}

double getDistance() {
    Vector3D pos;
    if(targetCheck(LockTarget)) {
        pos = LockTarget.Position;
    } else {
        pos = targetPosition;
    }
    if((MisShip != null) && (MisShip.Debug == "Normal")) {
        return Vector3D.Distance(pos, MisShip.Position);
    }
    return 1000;
}

// 使用包含指定字符串的方块组构建Ship
Ship newShipWithNameContantBlocks() {
    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType(blocks);
    Ship nShip = new Ship(blocks);
    Echo(nShip.Debug);
    return nShip;
}

bool targetCheck(Ship.Target t) {
    if(t != null && t.EntityId != 0)
        return true;
    else
        return false;
}

Program()
{
    _myBroadcastListener=IGC.RegisterBroadcastListener(AntennaTag);
    _myBroadcastListener.SetMessageCallback(AntennaTag); 
    MEA.GTS = GridTerminalSystem;
    MEA.PG = this;
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    Me.CustomData = "";
    // 获取导弹标识
    shipTag = int.Parse(Me.CustomName.Split(']')[0].Split('-')[1]);
    Echo("初始化完毕!");
}


/*
==== MEA 方块基类 v2.0 ====
作者：     lilimin
QQ：       461353919
MEA QQ群： 530683714
创作时间：  2019年12月14日 18:43:21

==== 关于游戏IngameAPI更新的总结 ====
1、在编程块运行类Program中，即当下文本全局挂载了一个IGC对象，类型IMyIntergridCommunicationSystem，用于处理类似天线通讯。并且天线通讯时，可以传输任意类型数据，传输方法使用的是泛型。
2、在编程块中，挂载了一个string Storage，它基于编程块这个方块进行保存，在代码修改保存后依然保留原有的值。
2、将所有显示屏独立为一个类，主控座椅、LCD上的显示屏全部独立为这个类。

==== 主要更新说明 ====
总览：这是一次大更新，之前版本的方块基类已经废了一部分，新版的方块基类会更适合开发新的项目。方块基类明确一个宗旨：让开发者以最方便的方式操作方块。

1、将所有类放入一个大的MEA类中，相当于加上一层命名空间，防止与其他脚本名称冲突
2、按照2019年4月左右的最新InGame API进行更新，修复了之前K社更新游戏导致的大量问题
3、在MEA类中定义两个静态属性（PG和GTS），分别代表编程块和GridTerminalSystem，用于类中调用它来获取方块或使用Echo等方法。
因此在使用MEA类之前，必须赋值，MEA.PG = this; MEA.GTS = GridTerminalSystem;
*/
public class MEA
{
    public static IMyGridTerminalSystem GTS;
    public static Program PG;

    /// <summary>
    /// 单一方块类
    /// </summary>
    public class Block
    {
        // == 构造方法
        public Block() { }
        public Block(Block b) { _block = b._block; }
        public Block(string name) { _block = GTS.GetBlockWithName(name); }
        public Block(IMyTerminalBlock block) { _block = block; }


        // == 成员属性
        public IMyTerminalBlock _block; //方块
        // 唯一ID
        public long Id { get { return _block.EntityId; } }
        //方块是否获取成功，当获取方块失败时，返回False
        public bool IsOK { get { return _block != null; } }
        //是否启用
        public bool OnOff
        {
            get { return _block is IMyFunctionalBlock ? (_block as IMyFunctionalBlock).Enabled : false; }
            set { if (_block is IMyFunctionalBlock) { (_block as IMyFunctionalBlock).Enabled = value; } }
        }
        //是否被黑了
        public bool IsBeingHacked { get { return _block.IsBeingHacked; } }
        //是否可以工作（健康度）
        public bool IsFunctional { get { return _block.IsFunctional; } }
        //是否在工作
        public bool IsWorking { get { return _block.IsWorking; } }
        //在网格中的顺序
        public int NumberInGrid { get { return _block.NumberInGrid; } }
        //所属网格
        public IMyCubeGrid Grid { get { return _block.CubeGrid; } }
        //名称（K表中名称，CustomName）
        public string Name
        {
            get { return _block.CustomName; }
            set { _block.CustomName = value; }
        }
        //自定义数据 （CustomData）
        public string Data
        {
            get { return _block.CustomData; }
            set { _block.CustomData = value; }
        }
        //所有人ID
        public long OwnerId { get { return _block.OwnerId; } }
        //坐标（基于世界绝对坐标系的坐标）
        public Vector3D Position { get { return _block.GetPosition(); } }
        //矩阵（描述方块世界坐标几方位的矩阵）
        public Matrix WorldMatrix { get { return _block.WorldMatrix; } }
        //前向矢量（归一化的，即矢量的长度恒等于1）
        public Vector3D Forward { get { return _block.WorldMatrix.Forward; } }
        //后向矢量（归一化的，即矢量的长度恒等于1）
        public Vector3D Backward { get { return _block.WorldMatrix.Backward; } }
        //左向矢量（归一化的，即矢量的长度恒等于1）
        public Vector3D Left { get { return _block.WorldMatrix.Left; } }
        //右向矢量（归一化的，即矢量的长度恒等于1）
        public Vector3D Right { get { return _block.WorldMatrix.Right; } }
        //上向矢量（归一化的，即矢量的长度恒等于1）
        public Vector3D Up { get { return _block.WorldMatrix.Up; } }
        //下向矢量（归一化的，即矢量的长度恒等于1）
        public Vector3D Down { get { return _block.WorldMatrix.Down; } }
        //是否在终端显示
        public bool ShowInTerminal { get { return _block.ShowInTerminal; } set { _block.ShowInTerminal = value; } }
        //是否在库存中显示
        public bool ShowInInventory { get { return _block.ShowInInventory; } set { _block.ShowInInventory = value; } }
        //是否在HUD上显示
        public bool ShowOnHUD { get { return _block.ShowOnHUD; } set { _block.ShowOnHUD = value; } }
    }

    /// <summary>
    /// 多个方块类
    /// </summary>
    public class Blocks : IEnumerator
    {
        // == 构造方法
        public Blocks() { }
        public Blocks(string name, Func<IMyTerminalBlock, bool> collect = null)
        {
            List<IMyTerminalBlock> search_blocks = new List<IMyTerminalBlock>();
            GTS.SearchBlocksOfName(name, search_blocks, collect);
            foreach (IMyTerminalBlock b in search_blocks)
            {
                _blocks.Add(new Block(b));
            }
        }
        public Blocks(List<Block> blocks)
        {
            foreach (Block b in blocks)
            {
                _blocks.Add(b);
            }
        }
        public Blocks(List<IMyTerminalBlock> blocks)
        {
            foreach (IMyTerminalBlock b in blocks)
            {
                _blocks.Add(new Block(b));
            }
        }

        // == 属性 ==
        public List<Block> _blocks = new List<Block>(); // 方块List
        public int _index = -1; // 当前指针下标

        public int Count
        { // 方块个数
            get
            {
                return _blocks.Count;
            }
        }

        // == 实现数组或List访问
        public Block this[int index]
        {
            get
            {
                try
                {
                    return _blocks[index];
                }
                catch (Exception e)
                {
                    throw e;
                }
            }
        }

        // == 实现IEnumerator接口的方法，目的是可以在foreach中使用
        public bool MoveNext() { _index++; return (_index < _blocks.Count); }
        public void Reset() { _index = 0; }
        object IEnumerator.Current
        {
            get
            {
                try
                {
                    return _blocks[_index];
                }
                catch (Exception e)
                {
                    throw e;
                }
            }
        }
        public IEnumerator GetEnumerator()
        {
            return this;
        }
        // 实现IEnumerator接口完成

        // == 模拟实现Linq相关方法
        /// <summary>
        /// 筛选
        /// </summary>
        /// <param name="collect"></param>
        public Blocks Where(Func<Block, bool> collect)
        {
            Blocks blocks = new Blocks();
            foreach (Block b in _blocks)
            {
                if (collect(b))
                {
                    blocks.Add(b);
                }
            }
            return blocks;
        }

        /// <summary>
        /// 查找
        /// </summary>
        /// <param name="collect"></param>
        public Block Find(Func<Block, bool> collect)
        {
            foreach (Block b in _blocks)
            {
                if (collect(b)) return b;
            }
            return null;
        }


        /// <summary>
        /// 添加一个方块
        /// </summary>
        /// <param name="b"></param>   
        public void Add(Block b)
        {
            _blocks.Add(b);
        }

        // 移除一个方块
        public bool Remove(Block b)
        {
            _blocks.Remove(b); return true;
        }
        public bool Remove(int index)
        {
            if (index >= 0 && index <= _blocks.Count - 1)
            {
                return Remove(_blocks[index]);
            }
            return false;
        }

        /// <summary>
        /// 转为List
        /// </summary>
        public List<Block> ToList()
        {
            List<Block> l = new List<Block>();
            foreach (Block b in _blocks)
            {
                l.Add(b);
            }
            return l;
        }
    }

    /// <summary>
    /// 显示屏幕类（LCD或Cockpit中的屏幕）
    /// </summary>
    public class DisplayScreen
    {
        // == 构造方法
        public DisplayScreen(IMyTextSurface s) { _screen = s; }

        // == 属性
        // 屏幕对象
        public IMyTextSurface _screen;
        // 字体大小
        public double FontSize
        {
            get { return (double)_screen.FontSize; }
            set { _screen.FontSize = (float)value; }
        }
        // 文字颜色
        public Color FontColor
        {
            get { return _screen.FontColor; }
            set { _screen.FontColor = value; }
        }
        // 背景颜色
        public Color BackgroundColor
        {
            get { return _screen.BackgroundColor; }
            set { _screen.BackgroundColor = value; }
        }
        // 背景透明度（类似int，范围0-255）
        public byte BackgroundAlpha
        {
            get { return _screen.BackgroundAlpha; }
            set { _screen.BackgroundAlpha = value; }
        }
        // 脚本背景颜色
        public Color ScriptBackgroundColor
        {
            get { return _screen.ScriptBackgroundColor; }
            set { _screen.ScriptBackgroundColor = value; }
        }
        // 脚本前景颜色
        public Color ScriptForegroundColor
        {
            get { return _screen.ScriptForegroundColor; }
            set { _screen.ScriptForegroundColor = value; }
        }
        // 切换时间间隔
        public double ChangeInterval
        {
            get { return (double)_screen.ChangeInterval; }
            set { _screen.ChangeInterval = (float)value; }
        }
        // 对齐方式(LEFT=0、RIGHT=1、CENTER=2)
        public TextAlignment Alignment
        {
            get { return _screen.Alignment; }
            set { _screen.Alignment = value; }
        }
        // 当前脚本
        public string Script
        {
            get { return _screen.Script; }
            set { _screen.Script = value; }
        }
        // 显示类型
        /*
        NONE = 0,
        TEXT_AND_IMAGE = 1,
        [Obsolete("Use TEXT_AND_IMAGE instead.")]
        IMAGE = 2,
        SCRIPT = 3
        */
        public VRage.Game.GUI.TextPanel.ContentType ContentType
        {
            get { return _screen.ContentType; }
            set { _screen.ContentType = value; }
        }
        // 像素尺寸
        public Vector2I SurfaceSize
        {
            get { return new Vector2I((int)_screen.SurfaceSize.X, (int)_screen.SurfaceSize.Y); }
        }
        // 图像尺寸
        public Vector2I TextureSize
        {
            get { return new Vector2I((int)_screen.TextureSize.X, (int)_screen.TextureSize.Y); }
        }
        // 保持长宽比
        public bool PreserveAspectRatio
        {
            get { return _screen.PreserveAspectRatio; }
            set { _screen.PreserveAspectRatio = value; }
        }
        // 当前图像
        public string CurrentlyShownImage
        {
            get { return _screen.CurrentlyShownImage; }
        }
        // 字体
        public string Font
        {
            get { return this._screen.Font; }
            set { this._screen.Font = value; }
        }
        // 文字内边距
        public double TextPadding
        {
            get { return (double)_screen.TextPadding; }
            set { _screen.TextPadding = (float)value; }
        }
        // 文字内容
        public string Text
        {
            get { return _screen.GetText(); }
            set { _screen.WriteText(value); }
        }
        // 屏幕名称
        public string Name
        {
            get { return _screen.Name; }
        }
        // 屏幕显示名称
        public string DisplayName
        {
            get { return _screen.DisplayName; }
        }

        // == 方法
        // 获取所有字体列表
        public void GetFonts(List<string> fonts) { _screen.GetFonts(fonts); }
        // 获取所有脚本列表
        public void GetScripts(List<string> scripts) { _screen.GetScripts(scripts); }
        // 获取选中的图像列表
        public void GetSelectedImages(List<string> output) { _screen.GetSelectedImages(output); }
    }

    /// <summary>
    /// LCD面板类
    /// 依赖Block类、DisplayScreen类
    /// </summary>
    public class LCD : Block
    {
        // == 构造方法
        public LCD() { }
        public LCD(string name) : base(name) { _lcd = _block as IMyTextPanel; Screen = new DisplayScreen(_lcd as IMyTextSurface); }
        public LCD(IMyTextPanel block) : base(block) { _lcd = _block as IMyTextPanel; }
        public LCD(LCD block) : base(block) { _lcd = _block as IMyTextPanel; }

        // LCD方块
        public IMyTextPanel _lcd;
        public DisplayScreen Screen;

        // K表上的标题
        public string Title
        {
            get { return _lcd.GetPublicTitle(); }
            set { _lcd.WritePublicTitle(value); }
        }
    }

    /// <summary>
    /// 主控座椅块
    /// 依赖Block类、DisplayScreen类
    /// </summary>
    public class Cockpit : Block
    {
        // == 构造方法
        public Cockpit() { }
        public Cockpit(string name) : base(name)
        {
            if (IsOK) _cockpit = _block as IMyCockpit;
            _initScreens();
        }
        public Cockpit(IMyTerminalBlock b) : base(b)
        {
            if (IsOK) _cockpit = _block as IMyCockpit;
            _initScreens();
        }

        protected void _initScreens()
        {
            List<DisplayScreen> ss = new List<DisplayScreen>();
            for (int i = 0; i < _cockpit.SurfaceCount; i++)
            {
                ss.Add(new DisplayScreen(_cockpit.GetSurface(i)));
            }
            Screens = ss;
        }

        // == 属性
        //主控方块
        public IMyCockpit _cockpit;
        //屏幕
        public List<DisplayScreen> Screens = new List<DisplayScreen>();
        //是否可以控制飞船
        public bool CanControlShip
        {
            get { return _cockpit.CanControlShip; }
        }
        //是否正在被玩家控制
        public bool IsUnderControl
        {
            get { return _cockpit.IsUnderControl; }
        }
        //是否有轮子
        public bool HasWheels
        {
            get { return _cockpit.HasWheels; }
        }
        //是否勾选了可以控制轮子
        public bool ControlWheels
        {
            get { return _cockpit.ControlWheels; }
            set { _cockpit.ControlWheels = value; }
        }
        //是否勾选了可以控制推进器
        public bool ControlThrusters
        {
            get { return _cockpit.ControlThrusters; }
            set { _cockpit.ControlThrusters = value; }
        }
        //是否勾选了手刹
        public bool HandBrake
        {
            get { return _cockpit.HandBrake; }
            set { _cockpit.HandBrake = value; }
        }
        //是否勾选了惯性抑制
        public bool DampenersOverride
        {
            get { return _cockpit.DampenersOverride; }
            set { _cockpit.DampenersOverride = value; }
        }
        //是否是主控
        public bool IsMainCockpit
        {
            get { return _cockpit.IsMainCockpit; }
            set { _cockpit.IsMainCockpit = value; }
        }
        //是否勾选了显示地平线高度
        public bool ShowHorizonIndicator
        {
            get { return _cockpit.ShowHorizonIndicator; }
            set { _cockpit.ShowHorizonIndicator = value; }
        }
        //按键输入，X表示AD，Y表示空格和C，Z表示SW
        public Vector3D InputKey
        {
            get
            {
                Vector3D temp = new Vector3D();
                Vector3D.TryParse(_cockpit.MoveIndicator.ToString(), out temp);
                return temp;
            }
        }
        //QE按键输入
        public double InputRoll
        {
            get { return (double)_cockpit.RollIndicator; }
        }
        //鼠标输入，X表示左右，Y表示上下
        public Vector2D InputMouse
        {
            get
            {
                return new Vector2D((double)_cockpit.RotationIndicator.X, (double)_cockpit.RotationIndicator.Y);
            }
        }
        //质心（不考虑活塞转子等外挂部分）
        public Vector3D CenterOfMass
        {
            get { return _cockpit.CenterOfMass; }
        }
        //飞船基础质量
        public double BaseMass
        {
            get { return _cockpit.CalculateShipMass().BaseMass; }
        }
        //飞船总体质量
        public double TotalMass
        {
            get { return _cockpit.CalculateShipMass().TotalMass; }
        }
        //飞船物理质量
        public double PhysicalMass
        {
            get { return _cockpit.CalculateShipMass().PhysicalMass; }
        }
        //人工重力
        public Vector3D ArtificialGravity
        {
            get { return _cockpit.GetArtificialGravity(); }
        }
        //自然重力
        public Vector3D NaturalGravity
        {
            get { return _cockpit.GetNaturalGravity(); }
        }
        //飞船线速度矢量
        public Vector3D Velocity
        {
            get { return _cockpit.GetShipVelocities().LinearVelocity; }
        }
        //飞船角速度矢量
        public Vector3D AngleVelocity
        {
            get { return _cockpit.GetShipVelocities().AngularVelocity; }
        }
        //飞船偏航Yaw速度（基于自己的坐标系，单位角度）
        public double YawVelocity
        {
            get
            {
                MatrixD refLookAtMatrix = MatrixD.CreateLookAt(new Vector3D(), WorldMatrix.Forward, WorldMatrix.Up);
                //求出自己当前角速度相对自己的值，这是一套弧度值，其中最大值是0.45，最小值是0.0005，x表示俯仰Pitch（上+下-），y表示偏航Yaw（左+右-），z表示滚转（顺时针-逆时针+）
                Vector3D MeAngleVelocityToMe = Vector3D.TransformNormal(AngleVelocity, refLookAtMatrix);
                return MeAngleVelocityToMe.Y * 180 / Math.PI;
            }
        }
        //飞船俯仰Pitch速度（基于自己的坐标系，单位角度）
        public double PitchVelocity
        {
            get
            {
                MatrixD refLookAtMatrix = MatrixD.CreateLookAt(new Vector3D(), WorldMatrix.Forward, WorldMatrix.Up);
                //求出自己当前角速度相对自己的值，这是一套弧度值，其中最大值是0.45，最小值是0.0005，x表示俯仰Pitch（上+下-），y表示偏航Yaw（左+右-），z表示滚转（顺时针-逆时针+）
                Vector3D MeAngleVelocityToMe = Vector3D.TransformNormal(AngleVelocity, refLookAtMatrix);
                return MeAngleVelocityToMe.X * 180 / Math.PI;
            }
        }
        //飞船滚转Roll速度（基于自己的坐标系，单位角度）
        public double RollVelocity
        {
            get
            {
                MatrixD refLookAtMatrix = MatrixD.CreateLookAt(new Vector3D(), WorldMatrix.Forward, WorldMatrix.Up);
                //求出自己当前角速度相对自己的值，这是一套弧度值，其中最大值是0.45，最小值是0.0005，x表示俯仰Pitch（上+下-），y表示偏航Yaw（左+右-），z表示滚转（顺时针-逆时针+）
                Vector3D MeAngleVelocityToMe = Vector3D.TransformNormal(AngleVelocity, refLookAtMatrix);
                return MeAngleVelocityToMe.Z * 180 / Math.PI;
            }
        }
        //是否在星球上
        public bool IsInPlanet
        {
            get
            {
                Vector3D temp = new Vector3D();
                return _cockpit.TryGetPlanetPosition(out temp);
            }
        }
        //所处的星球坐标（处于星球状态中才能获取）
        public Vector3D PlanetPosition
        {
            get
            {
                Vector3D temp = new Vector3D();
                _cockpit.TryGetPlanetPosition(out temp);
                return temp;
            }
        }
        //所处的星球海平面高度（处于星球状态中才能获取）
        public double PlanetElevationSealevel
        {
            get
            {
                double e = 0;
                _cockpit.TryGetPlanetElevation(MyPlanetElevation.Sealevel, out e);
                return e;
            }
        }
        //所处的星球地表高度（处于星球状态中才能获取）
        public double PlanetElevationSurface
        {
            get
            {
                double e = 0;
                _cockpit.TryGetPlanetElevation(MyPlanetElevation.Surface, out e);
                return e;
            }
        }
        //氧气容量
        public double OxygenCapacity
        {
            get { return (double)_cockpit.OxygenCapacity; }
        }
        //氧气填充率
        public double OxygenFilledRatio
        {
            get { return (double)_cockpit.OxygenFilledRatio; }
        }
    }

    /// <summary>
    /// 推进器
    /// 依赖Block类、BlockDirections类
    /// </summary>
    public class Thrust : Block
    {
        // == 构造方法
        public Thrust(IMyTerminalBlock b) : base(b) { _thrust = _block as IMyThrust; }
        public Thrust(Block b) : base(b) { _thrust = _block as IMyThrust; }
        public Thrust(string name) : base(name) { _thrust = _block as IMyThrust; }

        // == 成员属性
        public IMyThrust _thrust;
        //越级出力值（单位：百分比）
        public double Power
        {
            get { return _thrust.ThrustOverridePercentage; }
            set { _thrust.ThrustOverridePercentage = (float)value; }
        }
        //越级出力值（单位：牛顿）
        public double PowerN
        {
            get { return _thrust.ThrustOverride; }
            set { _thrust.ThrustOverride = (float)value; }
        }
        //最大出力值（单位：牛顿）
        public double MaxThrust
        {
            get { return _thrust.MaxThrust; }
        }
        //最大实际出力值（单位：牛顿）
        public double MaxEffectiveThrust
        {
            get { return _thrust.MaxEffectiveThrust; }
        }
        //当前出力值（单位：牛顿）
        public double CurrentThrust
        {
            get { return _thrust.CurrentThrust; }
        }
        //基于网格方向的相对方向，相当于安装方向
        public Vector3I GridThrustDirection
        {
            get { return _thrust.GridThrustDirection; }
        }
    }

    /// <summary>
    /// 方块方向枚举
    /// </summary>
    public enum BlockDirections
    {
        All = 0, Up = 1, Down = 2, Forward = 3, Backward = 4, Left = 5, Right = 6
    }

    /// <summary>
    /// 推进器组
    /// 依赖Thrust类，Blocks类
    /// </summary>
    public class Thrusts : Blocks, IEnumerator
    {
        // == 构造方法
        public Thrusts() : base() { InitThrusts(); }
        public Thrusts(string name, Func<IMyTerminalBlock, bool> collect = null) : base(name, collect) { InitThrusts(); }
        public Thrusts(List<Block> blocks) : base(blocks) { InitThrusts(); }
        public Thrusts(List<IMyTerminalBlock> blocks) : base(blocks) { InitThrusts(); }

        public void InitThrusts()
        {
            _thrusts = new List<Thrust>();
            foreach (Block b in _blocks)
            {
                _thrusts.Add(new Thrust(b));
            }
        }

        // == 属性
        // 方向识别用的方块
        public Block DirectionBlock;
        // 多个推进器
        public List<Thrust> _thrusts;
        // 推进器方向
        public List<BlockDirections> _fields;


        // == 方法
        // 初始化方位
        public void InitDirection(Block cp = null)
        {
            if (cp != null) DirectionBlock = cp;

            _fields = new List<BlockDirections>();
            for (int i = 0; i < _thrusts.Count; i++)
            {
                Base6Directions.Direction CockpitForward = _thrusts[i].WorldMatrix.GetClosestDirection(DirectionBlock.WorldMatrix.Forward);
                Base6Directions.Direction CockpitUp = _thrusts[i].WorldMatrix.GetClosestDirection(DirectionBlock.WorldMatrix.Up);
                Base6Directions.Direction CockpitLeft = _thrusts[i].WorldMatrix.GetClosestDirection(DirectionBlock.WorldMatrix.Left);
                switch (CockpitForward)
                { case Base6Directions.Direction.Forward: _fields.Add(BlockDirections.Forward); break; case Base6Directions.Direction.Backward: _fields.Add(BlockDirections.Backward); break; }
                switch (CockpitUp)
                { case Base6Directions.Direction.Forward: _fields.Add(BlockDirections.Up); break; case Base6Directions.Direction.Backward: _fields.Add(BlockDirections.Down); break; }
                switch (CockpitLeft)
                { case Base6Directions.Direction.Forward: _fields.Add(BlockDirections.Left); break; case Base6Directions.Direction.Backward: _fields.Add(BlockDirections.Right); break; }
            }
        }

        // 实现接口
        object IEnumerator.Current
        {
            get
            {
                try
                {
                    return _thrusts[_index];
                }
                catch (Exception e)
                {
                    throw e;
                }
            }
        }

        // 添加一个推进器
        public void Add(Thrust t)
        {
            base.Add(t);
            _thrusts.Add(t);
            InitDirection();
        }

        // 移除一个推进器
        public bool Remove(Thrust b)
        {
            base.Remove(b);
            _thrusts.Remove(b);
            InitDirection();
            return true;
        }
        public bool Remove(int index, bool over = false)
        {
            base.Remove(index);
            if (index >= 0 && index <= _thrusts.Count - 1)
            {
                _thrusts.Remove(_thrusts[index]);
            }
            return false;
        }

        // 遍历所有推进器
        public void Each(Func<Thrust, bool> call = null)
        {
            if (call == null) return;
            foreach (Thrust t in _thrusts)
            {
                call(t);
            }
        }

        // 设置出力值
        public void SetValue(double value, BlockDirections direction = 0, bool resetOthers = false, bool isPercent = true)
        {
            for (int i = 0; i < _thrusts.Count; i++)
            {
                if (direction == BlockDirections.All || _fields[i] == direction)
                {
                    if (isPercent) { _thrusts[i].Power = value; }
                    else { _thrusts[i].PowerN = value; }
                }
            }
        }

        // 设置方块开关
        public void SetOnOff(bool onoff)
        {
            Each(t => t.OnOff = onoff);
        }
    }

    /// <summary>
    /// 陀螺仪
    /// 依赖Block类
    /// </summary>
    public class Gyro : Block
    {
        // == 静态属性
        public static double MaxMotionValue = 30; //最大出力值

        // == 构造方法
        public Gyro(IMyTerminalBlock b) : base(b) { _gyro = _block as IMyGyro; }
        public Gyro(Block b) : base(b) { _gyro = _block as IMyGyro; }
        public Gyro(string name) : base(name) { _gyro = _block as IMyGyro; }

        // == 成员属性
        public IMyGyro _gyro; //陀螺仪方块本体
        public int MotionRatio = 1; //正反系数

        //能量
        public double Pwoer
        {
            get { return _gyro.GyroPower; }
            set { _gyro.GyroPower = (float)value; }
        }
        //是否开启越级
        public bool IsOverride
        {
            get { return _gyro.GyroOverride; }
            set { _gyro.GyroOverride = value; }
        }
        //偏航值，开启越级后有效
        public double Yaw
        {
            get { return _gyro.Yaw * MotionRatio; }
            set { _gyro.Yaw = (float)value * MotionRatio; }
        }
        //俯仰值，开启越级后有效
        public double Pitch
        {
            get { return _gyro.Pitch * MotionRatio; }
            set { _gyro.Pitch = (float)value * MotionRatio; }
        }
        //滚转值，开启越级后有效
        public double Roll
        {
            get { return _gyro.Roll * MotionRatio; }
            set { _gyro.Roll = (float)value * MotionRatio; }
        }
    }

    /// <summary>
    /// 陀螺仪组
    /// 依赖Gyro类、Blocks类
    /// </summary>
    public class Gyros : Blocks, IEnumerator
    {
        // == 构造方法
        public Gyros() : base() { InitGyros(); }
        public Gyros(string name, Func<IMyTerminalBlock, bool> collect = null) : base(name, collect) { InitGyros(); }
        public Gyros(List<Block> blocks) : base(blocks) { InitGyros(); }
        public Gyros(List<IMyTerminalBlock> blocks) : base(blocks) { InitGyros(); }

        // == 属性
        // 方向识别用的方块
        public Block DirectionBlock;
        public List<Gyro> _gyros;
        public List<string> _yawField;
        public List<string> _pitchField;
        public List<string> _rollField;
        public List<double> _yawFactor;
        public List<double> _pitchFactor;
        public List<double> _rollFactor;

        // == 方法
        public void InitGyros()
        {
            _gyros = new List<Gyro>();
            foreach (Block b in _blocks)
            {
                _gyros.Add(new Gyro(b));
            }
        }
        // 初始化方向
        public void InitDirection(Block b)
        {
            DirectionBlock = b;
            _yawField = new List<string>();
            _pitchField = new List<string>();
            _rollField = new List<string>();
            _yawFactor = new List<double>();
            _pitchFactor = new List<double>();
            _rollFactor = new List<double>();
            for (int i = 0; i < _gyros.Count; i++)
            {
                Base6Directions.Direction gyroUp = _gyros[i].WorldMatrix.GetClosestDirection(DirectionBlock.WorldMatrix.Up);
                Base6Directions.Direction gyroLeft = _gyros[i].WorldMatrix.GetClosestDirection(DirectionBlock.WorldMatrix.Left);
                Base6Directions.Direction gyroForward = _gyros[i].WorldMatrix.GetClosestDirection(DirectionBlock.WorldMatrix.Forward);
                switch (gyroUp)
                {
                    case Base6Directions.Direction.Up: _yawField.Add("Yaw"); _yawFactor.Add(1); break;
                    case Base6Directions.Direction.Down: _yawField.Add("Yaw"); _yawFactor.Add(-1); break;
                    case Base6Directions.Direction.Left: _yawField.Add("Pitch"); _yawFactor.Add(1); break;
                    case Base6Directions.Direction.Right: _yawField.Add("Pitch"); _yawFactor.Add(-1); break;
                    case Base6Directions.Direction.Forward: _yawField.Add("Roll"); _yawFactor.Add(-1); break;
                    case Base6Directions.Direction.Backward: _yawField.Add("Roll"); _yawFactor.Add(1); break;
                }
                switch (gyroLeft)
                {
                    case Base6Directions.Direction.Up: _pitchField.Add("Yaw"); _pitchFactor.Add(1); break;
                    case Base6Directions.Direction.Down: _pitchField.Add("Yaw"); _pitchFactor.Add(-1); break;
                    case Base6Directions.Direction.Left: _pitchField.Add("Pitch"); _pitchFactor.Add(1); break;
                    case Base6Directions.Direction.Right: _pitchField.Add("Pitch"); _pitchFactor.Add(-1); break;
                    case Base6Directions.Direction.Forward: _pitchField.Add("Roll"); _pitchFactor.Add(-1); break;
                    case Base6Directions.Direction.Backward: _pitchField.Add("Roll"); _pitchFactor.Add(1); break;
                }

                switch (gyroForward)
                {
                    case Base6Directions.Direction.Up: _rollField.Add("Yaw"); _rollFactor.Add(1); break;
                    case Base6Directions.Direction.Down: _rollField.Add("Yaw"); _rollFactor.Add(-1); break;
                    case Base6Directions.Direction.Left: _rollField.Add("Pitch"); _rollFactor.Add(1); break;
                    case Base6Directions.Direction.Right: _rollField.Add("Pitch"); _rollFactor.Add(-1); break;
                    case Base6Directions.Direction.Forward: _rollField.Add("Roll"); _rollFactor.Add(-1); break;
                    case Base6Directions.Direction.Backward: _rollField.Add("Roll"); _rollFactor.Add(1); break;
                }
            }
        }
        // 设置越级值
        public void SetValue(double yaw = 0, double pitch = 0, double roll = 0)
        {
            for (int i = 0; i < _gyros.Count; i++)
            {
                _gyros[i]._gyro.SetValue(_yawField[i], (float)yaw * (float)_yawFactor[i]);
                _gyros[i]._gyro.SetValue(_pitchField[i], (float)pitch * (float)_pitchFactor[i]);
                _gyros[i]._gyro.SetValue(_rollField[i], (float)roll * (float)_rollFactor[i]);
            }
        }
        public void SetYaw(double v)
        {
            for (int i = 0; i < _gyros.Count; i++)
            {
                _gyros[i]._gyro.SetValue(_yawField[i], (float)v * (float)_yawFactor[i]);

            }
        }
        public void SetPitch(double v)
        {
            for (int i = 0; i < _gyros.Count; i++)
            {
                _gyros[i]._gyro.SetValue(_pitchField[i], (float)v * (float)_pitchFactor[i]);
            }
        }
        public void SetRoll(double v)
        {
            for (int i = 0; i < _gyros.Count; i++)
            {
                _gyros[i]._gyro.SetValue(_rollField[i], (float)v * (float)_rollFactor[i]);
            }
        }
        //开关越级
        public void SetOverride(bool onoff = true)
        {
            for (int i = 0; i < this._gyros.Count; i++)
            {
                this._gyros[i].IsOverride = onoff;
            }
        }

        //开关方块
        public void SetOnOff(bool onoff = true)
        {
            for (int i = 0; i < this._gyros.Count; i++)
            {
                this._gyros[i].OnOff = onoff;
            }
        }
    }
}

/* Ship类 by: MEA */
public class Ship
{
	// ----- 静态属性 -----
	public static int timetick; //帧计时器。注意：这是一个静态变量，使用Ship.timetick访问它，它是这个类共有的变量而不是某个实例化后的飞船特有的。

	// ----- 方块类变量 -----
	public IMyShipController Cockpit;
	public IMyShipConnector Connector;
	public IMyShipConnector MotherConnector;
	public List<IMyLargeTurretBase> AutoWeapons = new List<IMyLargeTurretBase>();
	public List<IMySmallGatlingGun> GatlingGuns = new List<IMySmallGatlingGun>();
	public List<IMySmallMissileLauncher> RocketLaunchers = new List<IMySmallMissileLauncher>();
	public List<IMyCameraBlock> Cameras = new List<IMyCameraBlock>();
	public List<IMyGyro> Gyroscopes = new List<IMyGyro>();
	public List<IMyThrust> Thrusts = new List<IMyThrust>();
	public List<string> ThrustField = new List<string>();
	public List<string> gyroYawField = new List<string>();
	public List<string> gyroPitchField = new List<string>();
	public List<string> gyroRollField = new List<string>();
	public List<float> gyroYawFactor = new List<float>();
	public List<float> gyroPitchFactor = new List<float>();
	public List<float> gyroRollFactor = new List<float>();
	
	// ---- 必要配置变量 ----
	public string Debug = "Normal"; //错误报告，通过这个变量判断是否初始化成功
	
	// ----- 运动信息和相关变量 -----
	public Vector3D Position;
	public Vector3D Velocity;
	public Vector3D Acceleration;
	public double Diameter;
	
	// ---- 瞄准PID算法参数 ------
	public double AimRatio = 3; //瞄准精度，单位：度。用来是否瞄准，以便其他动作判断。不影响瞄准的效率。当瞄准块的正前方向量与瞄准块和目标的连线向量夹角小于这个值时，整个系统判定瞄准了目标。
	public int AimPID_T = 5; //PID 采样周期（单位：帧），周期越小效果越好，但太小的周期会让积分系数难以发挥效果
	public double AimPID_P = 0.8; //比例系数：可以理解为整个PID控制的总力度，建议范围0到1.2，1是完全出力。
	public double AimPID_I = 3; //积分系数：增加这个系数会让静态误差增加（即高速环绕误差），但会减少瞄准的震荡。反之同理
	public double AimPID_D = 10; //微分系数：增加这个系数会减少瞄准的震荡幅度，但会加剧在小角度偏差时的震荡幅度。反之同理
	
	// ----- 构造方法 ------
    // 传入方块后自动处理方块并实例化
	public Ship(){
        this.Debug = "Empty Init Ship";
    }
	public Ship(List<IMyTerminalBlock> Blocks)
	{
		//这里面可以写入更详细的判断方块是否需要获取的条件，例如名字匹配
		foreach(IMyTerminalBlock b in Blocks){
			if(b is IMyShipController){
				this.Cockpit = b as IMyShipController;
			}
			if(b is IMyCameraBlock){
				this.Cameras.Add(b as IMyCameraBlock);
			}
			if(b is IMyLargeTurretBase){
				this.AutoWeapons.Add(b as IMyLargeTurretBase);
			}
			if(b is IMySmallGatlingGun){
				this.GatlingGuns.Add(b as IMySmallGatlingGun);
			}
			if(b is IMySmallMissileLauncher){
				this.RocketLaunchers.Add(b as IMySmallMissileLauncher);
			}
			if(b is IMyGyro){
				this.Gyroscopes.Add(b as IMyGyro);
			}
			if(b is IMyThrust){
				this.Thrusts.Add(b as IMyThrust);
			}
			if(b is IMyShipConnector && (b as IMyShipConnector).OtherConnector != null){
				this.Connector = b as IMyShipConnector;
				this.MotherConnector = Connector.OtherConnector;
			}
		}
		
		if(Cockpit == null) {Debug = "Cockpit Not Found"; return;}
		Cameras.ForEach(delegate(IMyCameraBlock cam){cam.ApplyAction("OnOff_On");cam.EnableRaycast = true;});
		
		//处理推进器
		for(int i = 0; i < Thrusts.Count; i ++)   
		{
			Base6Directions.Direction CockpitForward = Thrusts[i].WorldMatrix.GetClosestDirection(Cockpit.WorldMatrix.Forward);   
			Base6Directions.Direction CockpitUp = Thrusts[i].WorldMatrix.GetClosestDirection(Cockpit.WorldMatrix.Up);   
			Base6Directions.Direction CockpitLeft = Thrusts[i].WorldMatrix.GetClosestDirection(Cockpit.WorldMatrix.Left);   
			switch (CockpitForward)   
			{ case Base6Directions.Direction.Forward: ThrustField.Add("Forward"); break; case Base6Directions.Direction.Backward: ThrustField.Add("Backward"); break; }   
			switch (CockpitUp)   
			{ case Base6Directions.Direction.Forward: ThrustField.Add("Up"); break; case Base6Directions.Direction.Backward: ThrustField.Add("Down"); break; }   
			switch (CockpitLeft)   
			{ case Base6Directions.Direction.Forward: ThrustField.Add("Left"); break; case Base6Directions.Direction.Backward: ThrustField.Add("Right"); break; }
			
			Thrusts[i].ApplyAction("OnOff_On");
		}
		
		//处理陀螺仪
		for (int i = 0; i < Gyroscopes.Count; i++)   
		{   
			Base6Directions.Direction gyroUp = Gyroscopes[i].WorldMatrix.GetClosestDirection(Cockpit.WorldMatrix.Up);   
			Base6Directions.Direction gyroLeft = Gyroscopes[i].WorldMatrix.GetClosestDirection(Cockpit.WorldMatrix.Left);   
			Base6Directions.Direction gyroForward = Gyroscopes[i].WorldMatrix.GetClosestDirection(Cockpit.WorldMatrix.Forward);   
			switch (gyroUp)   
			{ case Base6Directions.Direction.Up: gyroYawField.Add("Yaw"); gyroYawFactor.Add(1f); break;   
			  case Base6Directions.Direction.Down: gyroYawField.Add("Yaw"); gyroYawFactor.Add(-1f); break;   
			  case Base6Directions.Direction.Left: gyroYawField.Add("Pitch"); gyroYawFactor.Add(1f); break;   
			  case Base6Directions.Direction.Right: gyroYawField.Add("Pitch"); gyroYawFactor.Add(-1f); break;   
			  case Base6Directions.Direction.Forward: gyroYawField.Add("Roll"); gyroYawFactor.Add(-1f); break;   
			  case Base6Directions.Direction.Backward: gyroYawField.Add("Roll"); gyroYawFactor.Add(1f); break;   
			}   
			switch (gyroLeft)   
			{ case Base6Directions.Direction.Up: gyroPitchField.Add("Yaw"); gyroPitchFactor.Add(1f); break;   
			  case Base6Directions.Direction.Down: gyroPitchField.Add("Yaw"); gyroPitchFactor.Add(-1f); break;   
			  case Base6Directions.Direction.Left: gyroPitchField.Add("Pitch"); gyroPitchFactor.Add(1f); break;   
			  case Base6Directions.Direction.Right: gyroPitchField.Add("Pitch"); gyroPitchFactor.Add(-1f); break;   
			  case Base6Directions.Direction.Forward: gyroPitchField.Add("Roll"); gyroPitchFactor.Add(-1f); break;   
			  case Base6Directions.Direction.Backward: gyroPitchField.Add("Roll"); gyroPitchFactor.Add(1f); break;   
			}   
   
			switch (gyroForward)   
			{ case Base6Directions.Direction.Up: gyroRollField.Add("Yaw"); gyroRollFactor.Add(1f); break;   
			  case Base6Directions.Direction.Down: gyroRollField.Add("Yaw"); gyroRollFactor.Add(-1f); break;   
			  case Base6Directions.Direction.Left: gyroRollField.Add("Pitch"); gyroRollFactor.Add(1f); break;   
			  case Base6Directions.Direction.Right: gyroRollField.Add("Pitch"); gyroRollFactor.Add(-1f); break;   
			  case Base6Directions.Direction.Forward: gyroRollField.Add("Roll"); gyroRollFactor.Add(-1f); break;   
			  case Base6Directions.Direction.Backward: gyroRollField.Add("Roll"); gyroRollFactor.Add(1f); break;   
			}
			
			Gyroscopes[i].ApplyAction("OnOff_On");
		}
	}
	
    /* ===== 摄像头扫描相关 ===== */
    
	// ----- 摄像头搜索某个坐标点 -----
	// 持续搜索座标
	private int SP_Now_i;
	public Target ScanPoint(Vector3D Point){
		MyDetectedEntityInfo FoundTarget = new MyDetectedEntityInfo();
		
		List<IMyCameraBlock> RightAngleCameras = Ship.GetCanScanCameras(this.Cameras, Point);
		if(SP_Now_i >= RightAngleCameras.Count){SP_Now_i=0;}
		double ScanSpeed = (RightAngleCameras.Count*2000)/(Vector3D.Distance(Point, this.Position)*60);//每个循环可用于扫描的摄像头个数   
		   
		if(ScanSpeed >= 1)//每循环可扫描多个   
		{
			FoundTarget = RightAngleCameras[SP_Now_i].Raycast(Point);   
			SP_Now_i += 1;
			if(SP_Now_i >= RightAngleCameras.Count){SP_Now_i=0;}
		}   
		else
		{
			if(Ship.timetick%Math.Round(1/ScanSpeed,0)==0)
			{ 
				FoundTarget = RightAngleCameras[SP_Now_i].Raycast(Point);
				SP_Now_i += 1;
			}   
		}
		return new Target(FoundTarget);
	}
	
	// ----- 摄像头对多个坐标点执行脉冲扫描 -----
    // 返回第一个扫描到的目标后跳出
    // 可选传入一个EntityId作为匹配项
	public Target PulseScanSingle(List<Vector3D> Points, long EntityId = 0){
		MyDetectedEntityInfo FoundTarget = new MyDetectedEntityInfo();
		
		int x = 0;//这样做是为了减少不必要的运算量
		foreach(Vector3D p in Points){
			for(int i = x; i < this.Cameras.Count; i ++){
				if(this.Cameras[i].CanScan(p)){
					FoundTarget = this.Cameras[i].Raycast(p);
					if(!FoundTarget.IsEmpty()){
						if(EntityId == 0){
							return new Target(FoundTarget);
						}
						else if(FoundTarget.EntityId == EntityId){
							return new Target(FoundTarget);
						}
					}
					x = i;
					break;
				}
			}
		}
		
		return new Target(FoundTarget);
	}
	
    // ----- 摄像头对多个坐标进行完全脉冲扫描 -----
    // 将瞬间执行完整的扫描并返回所有扫描到的目标 （运算量较大）
	public List<Target> PulseScanMultiple(List<Vector3D> Points){
        List<Target> Res = new List<Target>();
		List<MyDetectedEntityInfo> FoundTargets = new List<MyDetectedEntityInfo>();
		
		int x = 0;//这样做是为了减少不必要的运算量
		foreach(Vector3D p in Points){
			for(int i = x; i < this.Cameras.Count; i ++){
				if(this.Cameras[i].CanScan(p)){
					MyDetectedEntityInfo FoundTarget = this.Cameras[i].Raycast(p);
					if(!FoundTarget.IsEmpty()){
						FoundTargets.Add(FoundTarget);
					}
					x = i;
					break;
				}
			}
		}
        foreach(MyDetectedEntityInfo t in FoundTargets){
            Res.Add(new Target(t));
        }
		
		return Res;
	}
	
	// ----- 追踪给定目标 -----
    // 可传入Target类或MyDetectedEntityInfo类
	private int TT_Now_i;
	public Vector3D TrackDeviationToTarget; //基于目标坐标系的偏移量，用来修正中心锁定的问题
	public Target TrackTarget(Target Tgt){
		
		MyDetectedEntityInfo FoundTarget = new MyDetectedEntityInfo();
		
		Vector3D posmove = Vector3D.TransformNormal(this.TrackDeviationToTarget, Tgt.Orientation);
		
		Vector3D lidarHitPoint = Tgt.Position + posmove + (Ship.timetick - Tgt.TimeStamp)*Tgt.Velocity/60; //这个碰撞点算法是最正确的
		
		List<IMyCameraBlock> RightAngleCameras = Ship.GetCanScanCameras(this.Cameras, lidarHitPoint);//获取方向正确的摄像头数量
		if(TT_Now_i >= RightAngleCameras.Count){TT_Now_i=0;}
	   
	    //执行常规追踪
		double ScanSpeed = (RightAngleCameras.Count*2000)/((Vector3D.Distance(lidarHitPoint, this.Position))*60);//每个循环可用于扫描的摄像头个数		
		if(ScanSpeed >= 1)
		{
			for(int i = 1; i < ScanSpeed; i ++){
				FoundTarget = RightAngleCameras[TT_Now_i].Raycast(lidarHitPoint);
				TT_Now_i += 1;
				if(TT_Now_i >= RightAngleCameras.Count){TT_Now_i=0;}
				if(!FoundTarget.IsEmpty() && FoundTarget.EntityId == Tgt.EntityId){
					return new Target(FoundTarget);
				}
			}
		}   
		else
		{
			//这里向上取整实际上是采用了更低一点的频率在扫描，有利于恢复储能
			if(Ship.timetick%Math.Ceiling(1/ScanSpeed)==0)   
			{
				FoundTarget = RightAngleCameras[TT_Now_i].Raycast(lidarHitPoint);
				TT_Now_i += 1;
				if(TT_Now_i >= RightAngleCameras.Count){TT_Now_i=0;}
				if(!FoundTarget.IsEmpty() && FoundTarget.EntityId == Tgt.EntityId){
					return new Target(FoundTarget);
				}
				
				//常规未找到，继续遍历摄像头进行搜索
				if(FoundTarget.IsEmpty() || FoundTarget.EntityId != Tgt.EntityId){
					for(int i = 0; i < RightAngleCameras.Count; i ++){
						FoundTarget = RightAngleCameras[TT_Now_i].Raycast(lidarHitPoint);
						TT_Now_i += 1;
						if(TT_Now_i >= RightAngleCameras.Count){TT_Now_i=0;}
						if(!FoundTarget.IsEmpty() && FoundTarget.EntityId == Tgt.EntityId){
							return new Target(FoundTarget);
						}
					}
				}
			}
		}
			//遍历搜索也未找到，进行脉冲阵面扫描
		if(FoundTarget.IsEmpty() || FoundTarget.EntityId != Tgt.EntityId){
			if(ScanSpeed >= 1 || Ship.timetick%Math.Ceiling(1/ScanSpeed)==0){
				int LostTick = Ship.timetick - Tgt.TimeStamp;
				double S_Radius = Tgt.Diameter*1.5; //搜索半径为目标直径1.5倍
				double S_Interval = Tgt.Diameter/5; //搜索间隙是目标直径的1/5
				Vector3D CenterPoint = Tgt.Position + Tgt.Velocity*LostTick/60 + Vector3D.Normalize(Tgt.Position - this.Position)*Tgt.Diameter/2;
				List<Vector3D> Points = new List<Vector3D>();
				Points.Add(CenterPoint);
				
				//这里计算出与飞船和目标连线垂直，且互相垂直的两个向量，用作x和y方向遍历
				Vector3D Vertical_X = Ship.CaculateVerticalVector((CenterPoint - this.Position), CenterPoint);
				Vector3D Vertical_Y = Vector3D.Normalize(Vector3D.Cross((CenterPoint - this.Position), Vertical_X));
				for(double x = 0; x < S_Radius; x += S_Interval){
					for(double y = 0; y < S_Radius; y += S_Interval){
						Points.Add(CenterPoint + Vertical_X*x + Vertical_Y*y);
						Points.Add(CenterPoint + Vertical_X*(-x) + Vertical_Y*y);
						Points.Add(CenterPoint + Vertical_X*x + Vertical_Y*(-y));
						Points.Add(CenterPoint + Vertical_X*(-x) + Vertical_Y*(-y));
					}
				}
				
				FoundTarget = this.PulseScanSingle(Points, Tgt.EntityId).EntityInfo;
				if(!FoundTarget.IsEmpty() && FoundTarget.EntityId == Tgt.EntityId){
					MatrixD TargetMainMatrix = FoundTarget.Orientation;   
					MatrixD TargetLookAtMatrix = MatrixD.CreateLookAt(new Vector3D(), TargetMainMatrix.Forward, TargetMainMatrix.Up);
					Vector3D hitpoint = new Vector3D();
					Vector3D.TryParse(FoundTarget.HitPosition.ToString(), out hitpoint);
					hitpoint = hitpoint + Vector3D.Normalize(hitpoint - this.Position)*2;
					this.TrackDeviationToTarget = Vector3D.TransformNormal(hitpoint - FoundTarget.Position, TargetLookAtMatrix);
					return new Target(FoundTarget);
				}
			}
		}
		
		return new Target(FoundTarget);
	}
	
    /* ===== 运动控制相关 ===== */
    
	// ----- 运动方块还原 -----
	public void MotionInit(){
		this.SetThrustOverride("All",0);
		this.SetGyroOverride(false);
	}
	
    // ----- 更新飞船物理信息 -----
    // 会自动更新本飞船的基本物理状态信息和时钟变量timetick
	public void UpdatePhysical(){
		this.Diameter = (this.Cockpit.CubeGrid.Max - this.Cockpit.CubeGrid.Min).Length() * this.Cockpit.CubeGrid.GridSize;
		this.Acceleration = ((this.Cockpit.GetPosition() - this.Position) * 60 - this.Velocity) * 60;
		this.Velocity = (this.Cockpit.GetPosition() - this.Position) * 60;
		this.Position = this.Cockpit.GetPosition();
	}
	
	// ----- 瞄准坐标点 -----
	// 使用PID算法，控制陀螺仪瞄准
    // 返回是否瞄准完成
	private List<Vector3D> Aim_PID_Data = new List<Vector3D>();
	public bool AimAtPosition(Vector3D TargetPos)
	{
		MatrixD refLookAtMatrix = MatrixD.CreateLookAt(new Vector3D(), this.Cockpit.WorldMatrix.Forward, this.Cockpit.WorldMatrix.Up);
		Vector3D PositionToMe = Vector3D.Normalize(Vector3D.TransformNormal(TargetPos - this.Position, refLookAtMatrix));
		
		//储存采样点
		if(Aim_PID_Data.Count < AimPID_T){
			for(int i = 0; i < AimPID_T; i ++){
				Aim_PID_Data.Add(new Vector3D());
			}
		}
		else{Aim_PID_Data.Remove(Aim_PID_Data[0]); Aim_PID_Data.Add(PositionToMe);}
		
		//获得采样点积分
		double X_I = 0;
		double Y_I = 0;
		foreach(Vector3D datapoint in Aim_PID_Data){
			X_I += datapoint.X;
			Y_I += datapoint.Y;
		}
		
		//计算输出
		double YawValue = AimPID_P*(PositionToMe.X + (1/AimPID_I)*X_I + AimPID_D*(Aim_PID_Data[AimPID_T-1].X - Aim_PID_Data[0].X)/AimPID_T) * 60;
		double PitchValue = AimPID_P*(PositionToMe.Y + (1/AimPID_I)*Y_I + AimPID_D*(Aim_PID_Data[AimPID_T-1].Y - Aim_PID_Data[0].Y)/AimPID_T) * 60;
		this.SetGyroValue(YawValue, PitchValue, 0);
		this.SetGyroOverride(true);
		
		// 计算当前与预期瞄准点的瞄准夹角
		Vector3D V_A = TargetPos - this.Position;
		Vector3D V_B = this.Cockpit.WorldMatrix.Forward;
		double Angle = Math.Acos(Vector3D.Dot(V_A,V_B)/(V_A.Length() * V_B.Length())) * 180 / Math.PI;
		if(Angle <= AimRatio){return true;}
		else{return false;}
	}
	
	// ----- 导航到坐标点 -----
	// 支持传入一个参考速度来跟踪目标进行速度匹配
    // 返回是否导航完成
	// 依赖SingleDirectionThrustControl()方法来执行对xyz三轴的独立运算，运算考虑了推进器是否可以工作，但不考虑供电带来的效率问题。
	// 本方法的结果路径是一个加速-减速-停止路径，通常不会错过目标，在此前提下本方法时间最优，在减速阶段处于对向推进器频繁满载震荡状态，在物理结果上是匀减速运动。
	public bool NavigationTo(Vector3D Pos, Vector3D TargetVelocity = new Vector3D())
	{
		double ShipMass = this.Cockpit.CalculateShipMass().PhysicalMass;
		//这个ThrustsPowers经过计算后，分别代表前后左右上下6个方向的理论最大加速度
		double[] ThrustsPowers = new double[6];
		for(int i = 0; i < this.Thrusts.Count; i ++){
			if(this.Thrusts[i].IsFunctional){
				switch(this.ThrustField[i]){
					case("Backward"): ThrustsPowers[0] += this.Thrusts[i].MaxEffectiveThrust; break;
					case("Forward"): ThrustsPowers[1] += this.Thrusts[i].MaxEffectiveThrust; break;
					case("Right"): ThrustsPowers[2] += this.Thrusts[i].MaxEffectiveThrust; break;
					case("Left"): ThrustsPowers[3] += this.Thrusts[i].MaxEffectiveThrust; break;
					case("Down"): ThrustsPowers[4] += this.Thrusts[i].MaxEffectiveThrust; break;
					case("Up"): ThrustsPowers[5] += this.Thrusts[i].MaxEffectiveThrust; break;
				}
			}
		}
		for(int i = 0; i < ThrustsPowers.Length; i ++){
			ThrustsPowers[i] /= ShipMass;
		}
		
		MatrixD refLookAtMatrix = MatrixD.CreateLookAt(new Vector3D(), this.Cockpit.WorldMatrix.Forward, this.Cockpit.WorldMatrix.Up);
		//这里的PosToMe表示目标坐标基于自己坐标系的座标，x左-右+，y下-上+，z前-后+，MeVelocityToMe使用相同的坐标系规则，表示自己的速度基于自己坐标系
		Vector3D PosToMe =  Vector3D.TransformNormal(Pos - this.Position, refLookAtMatrix);
		Vector3D MeVelocityToMe = Vector3D.TransformNormal(this.Velocity, refLookAtMatrix);
		
		this.SingleDirectionThrustControl(PosToMe.Z, MeVelocityToMe.Z, ThrustsPowers[0], ThrustsPowers[1], "Backward", "Forward", 0.5);
		this.SingleDirectionThrustControl(PosToMe.X, MeVelocityToMe.X, ThrustsPowers[2], ThrustsPowers[3], "Right", "Left", 0.5);
		this.SingleDirectionThrustControl(PosToMe.Y, MeVelocityToMe.Y, ThrustsPowers[5], ThrustsPowers[4], "Up", "Down", 0.5);
		
		if(TargetVelocity.Length() != 0){
			Vector3D TargetVelocityToMe = Vector3D.TransformNormal(TargetVelocity - this.Velocity, refLookAtMatrix);
			if(TargetVelocityToMe.X > 0){SetThrustOverride("Left", 100);}else if(TargetVelocityToMe.X < 0){SetThrustOverride("Right", 100);}
			if(TargetVelocityToMe.Y > 0){SetThrustOverride("Down", 100);}else if(TargetVelocityToMe.Y < 0){SetThrustOverride("Up", 100);}
			if(TargetVelocityToMe.Z > 0){SetThrustOverride("Forward", 100);}else if(TargetVelocityToMe.X < 0){SetThrustOverride("Backward", 100);}
		}
		
		if(PosToMe.Length() <= 1){return true;}
		return false;
	}
	
    // ----- 导航到坐标点（辅助方法） -----
	// 用于辅助NavigationTo()方法
	// 传入基于自己坐标系的目标单向方向，自己的单向速度，正向加速度，反向加速度，正向推进器方向名，反向推进器方向名，导航精度
	public void SingleDirectionThrustControl(double PosToMe, double VelocityToMe, double PostiveMaxAcceleration, double NagtiveMaxAcceleration, string PostiveThrustDirection, string NagtiveThrustDirection, double StopRatio){
		if(PosToMe < -StopRatio){
			double StopTime = -VelocityToMe/NagtiveMaxAcceleration;
			if(StopTime < 0){
				this.SetThrustOverride(PostiveThrustDirection, 100);
				this.SetThrustOverride(NagtiveThrustDirection, 0);
			}
			else{
				double StopDistance = -VelocityToMe*StopTime + 0.5*NagtiveMaxAcceleration*StopTime*StopTime;
				if(Math.Abs(PosToMe) > StopDistance){
					this.SetThrustOverride(PostiveThrustDirection, 100);
					this.SetThrustOverride(NagtiveThrustDirection, 0);
				}
				else{
					this.SetThrustOverride(PostiveThrustDirection, 0);
					this.SetThrustOverride(NagtiveThrustDirection, 100);
				}
			}
		}
		else if(PosToMe > StopRatio){
			double StopTime = VelocityToMe/NagtiveMaxAcceleration;
			if(StopTime < 0){
				//此时目标在后，运动速度是向前的
				this.SetThrustOverride(PostiveThrustDirection, 0);
				this.SetThrustOverride(NagtiveThrustDirection, 100);
			}
			else{
				double StopDistance = VelocityToMe*StopTime + 0.5*NagtiveMaxAcceleration*StopTime*StopTime;
				if(Math.Abs(PosToMe) > StopDistance){
					//实际距离大于刹车距离，执行推进
					this.SetThrustOverride(PostiveThrustDirection, 0);
					this.SetThrustOverride(NagtiveThrustDirection, 100);
				}
				else{
					//实际距离小于刹车距离，执行刹车
					this.SetThrustOverride(PostiveThrustDirection, 100);
					this.SetThrustOverride(NagtiveThrustDirection, 0);
				}
			}
		}
		else{
			this.SetThrustOverride(PostiveThrustDirection, 0);
			this.SetThrustOverride(NagtiveThrustDirection, 0);
		}
	}
	
    /* ===== 方块控制相关 ===== */
	// ----- 基础控制类方法 -----
	public void TurnBlocksOnOff(List<IMyTerminalBlock> B, bool o)
	{foreach(var b in B){b.ApplyAction(o?"OnOff_On":"OnOff_Off");}}
	public void TurnBlocksOnOff(List<IMyGyro> B, bool o)
    {foreach(var b in B){b.ApplyAction(o?"OnOff_On":"OnOff_Off");}}
	public void TurnBlocksOnOff(List<IMyThrust> B, bool o)
    {foreach(var b in B){b.ApplyAction(o?"OnOff_On":"OnOff_Off");}}
	public void TurnBlocksOnOff(List<IMyLargeTurretBase> B, bool o)
    {foreach(var b in B){b.ApplyAction(o?"OnOff_On":"OnOff_Off");}}
	
    // ----- 控制推进器越级 -----
    // 可传入Direcation = "All"、"Backward"、"Forward"、"Left"、"Right"、"Up"、"Down"
	public void SetThrustOverride(string Direction, double Value)   
	{
		if(Value > 100){Value = 100;}
		if(Value < 0){Value = 0;}
		for(int i = 0; i < this.Thrusts.Count; i ++){
			if(Direction == "All"){this.Thrusts[i].ThrustOverridePercentage = (float)Value;}
			else{if(this.ThrustField[i] == Direction){this.Thrusts[i].ThrustOverridePercentage = (float)Value;}}
		}
	}

    // ----- 开关陀螺仪越级 -----
	public void SetGyroOverride(bool bOverride)   
	{foreach(IMyGyro g in this.Gyroscopes){g.GyroOverride = bOverride;}}

    // ----- 控制陀螺仪越级 -----
    // 传入基于主控Cockpit的Yaw Pitch Roll，自动检测所有陀螺仪执行对应控制
	public void SetGyroValue(double Y, double P, double R)
	{
		for (int i = 0; i < this.Gyroscopes.Count; i++){
			this.Gyroscopes[i].SetValue(gyroYawField[i], (float)Y * gyroYawFactor[i]);
			this.Gyroscopes[i].SetValue(gyroPitchField[i], (float)P * gyroPitchFactor[i]);
			this.Gyroscopes[i].SetValue(gyroRollField[i], (float)R * gyroRollFactor[i]);
		}
	}

    // ----- 控制陀螺仪越级 -----
    // 可传入基于主控Cockpit的Yaw、Pitch、Roll中的单个轴进行控制，而不影响其他轴的状态
	public void SetGyroValue(string Field, double Value)
	{
		switch(Field){
			case("Yaw"):
			for (int i = 0; i < this.Gyroscopes.Count; i++){
				this.Gyroscopes[i].SetValue(gyroYawField[i], (float)Value * gyroYawFactor[i]);
			}
			break;
			case("Pitch"):
			for (int i = 0; i < this.Gyroscopes.Count; i++){
				this.Gyroscopes[i].SetValue(gyroPitchField[i], (float)Value * gyroPitchFactor[i]);
			}
			break;
			case("Roll"):
			for (int i = 0; i < this.Gyroscopes.Count; i++){
				this.Gyroscopes[i].SetValue(gyroRollField[i], (float)Value * gyroRollFactor[i]);
			}
			break;
		}
		
	}

    // ----- 让所有武器射击一次 -----
	public void WeaponsShoot()
	{
		this.GatlingGuns.ForEach(delegate(IMySmallGatlingGun g){g.ApplyAction("ShootOnce");});
		this.RocketLaunchers.ForEach(delegate(IMySmallMissileLauncher g){g.ApplyAction("ShootOnce");});
	}
	
	/* ===== Target类 ===== */
    // 由于摄像头、探测器等获取到的目标是 MyDetectedEntityInfo 类型，不方便计算和更新
    // 本类中统一将其转换为Target类进行操作
	public class Target
	{
		public string Name; //名字
		public long EntityId; //唯一ID，当这个值为0可判断该Target是空的
		public double Diameter; //半径
		public int TimeStamp; //记录时间戳、基于Ship类中的timetick的值来记录
		public Vector3D Position;
		public Vector3D Velocity;
		public Vector3D Acceleration;
		public Vector3D HitPosition;
		public MatrixD Orientation;
		public Vector3D AccurateLockPositionToTarget;
        public MyDetectedEntityInfo EntityInfo;
		
		public Target(){
			this.EntityId = 0;
			this.TimeStamp = 0;
            this.EntityInfo = new MyDetectedEntityInfo();
		}
		public Target(MyDetectedEntityInfo thisEntity){
			this.EntityId = thisEntity.EntityId;
			this.Name = thisEntity.Name;
			this.Diameter = Vector3D.Distance(thisEntity.BoundingBox.Max, thisEntity.BoundingBox.Min)/2;
			Vector3D.TryParse(thisEntity.Position.ToString(), out this.Position);
			Vector3D.TryParse(thisEntity.Velocity.ToString(), out this.Velocity);
			Vector3D.TryParse(thisEntity.HitPosition.ToString(), out this.HitPosition);
			this.Acceleration = new Vector3D();
			this.Orientation = thisEntity.Orientation;
			this.TimeStamp = Ship.timetick;
            this.EntityInfo = thisEntity;
		}
		public void UpdatePhysical(MyDetectedEntityInfo NewInfo){
			this.Diameter = Vector3D.Distance(NewInfo.BoundingBox.Max, NewInfo.BoundingBox.Min)/2;
			Vector3D.TryParse(NewInfo.Position.ToString(), out this.Position);
			Vector3D.TryParse(NewInfo.HitPosition.ToString(), out this.HitPosition);
			Vector3D velocity = new Vector3D();
			Vector3D.TryParse(NewInfo.Velocity.ToString(), out velocity);
			this.Acceleration = (velocity - this.Velocity)*60/(Ship.timetick - this.TimeStamp > 0 ? Ship.timetick - this.TimeStamp : 1);
			this.Velocity = velocity;
			this.Orientation = NewInfo.Orientation;
			this.TimeStamp = Ship.timetick;
            this.EntityInfo = NewInfo;
		}
        public void UpdatePhysical(Target _T){
            this.UpdatePhysical(_T.EntityInfo);
        }
        public bool IsEmpty(){
            return this.EntityId == 0;
        }
	}
	
    /* ===== 静态方法 ===== */
	// 静态方法通常是一些纯计算的方法，静态方法属于Ship这个类而不是某个实例化出来的Ship对象。
	// 使用静态方法时，请直接使用 Ship.GetCanScanCameras()，而不是对实例化出来的某个Ship实体使用。
	
	// -- 按FCS 8.0通讯标准读取一条参数 --
	static string GetOneInfo(string CustomData, string Title, string ArgName){
		string[] infos = CustomData.Split('\n');
		bool right = false;
		for(int i = 0; i < infos.Length; i ++){
			if(infos[i].Contains("[" + Title + "]")){
				right = true;
			}
			if(right && infos[i].Split('=')[0] == ArgName){
				return infos[i].Split('=')[1];
			}
			if(infos[i].Contains("[\\" + Title + "]")){
				break;
			}
		}
		return "";
	}
	
	// -- 计算子弹碰撞点 --
	public static Vector3D HitPointCaculate(Vector3D Me_Position, Vector3D Me_Velocity, Vector3D Me_Acceleration, Vector3D Target_Position, Vector3D Target_Velocity, Vector3D Target_Acceleration,    
								double Bullet_InitialSpeed, double Bullet_Acceleration, double Bullet_MaxSpeed)   
	{   
		//迭代算法   
		Vector3D HitPoint = new Vector3D();   
		Vector3D Smt = Target_Position - Me_Position;//发射点指向目标的矢量   
		Vector3D Velocity = Target_Velocity - Me_Velocity; //目标飞船和自己飞船总速度   
		Vector3D Acceleration = Target_Acceleration; //目标飞船和自己飞船总加速度   
		
		double AccTime = (Bullet_Acceleration == 0 ? 0 : (Bullet_MaxSpeed - Bullet_InitialSpeed)/Bullet_Acceleration);//子弹加速到最大速度所需时间   
		double AccDistance = Bullet_InitialSpeed*AccTime + 0.5*Bullet_Acceleration*AccTime*AccTime;//子弹加速到最大速度经过的路程   
		
		double HitTime = 0;   
		
		if(AccDistance < Smt.Length())//目标在炮弹加速过程外   
		{   
			HitTime = (Smt.Length() - Bullet_InitialSpeed*AccTime - 0.5*Bullet_Acceleration*AccTime*AccTime + Bullet_MaxSpeed*AccTime)/Bullet_MaxSpeed;   
			HitPoint = Target_Position + Velocity*HitTime + 0.5*Acceleration*HitTime*HitTime;   
		}   
		else//目标在炮弹加速过程内 
		{   
			double HitTime_Z = (-Bullet_InitialSpeed + Math.Pow((Bullet_InitialSpeed*Bullet_InitialSpeed + 2*Bullet_Acceleration*Smt.Length()),0.5))/Bullet_Acceleration;   
			double HitTime_F = (-Bullet_InitialSpeed - Math.Pow((Bullet_InitialSpeed*Bullet_InitialSpeed + 2*Bullet_Acceleration*Smt.Length()),0.5))/Bullet_Acceleration;   
			HitTime = (HitTime_Z > 0 ? (HitTime_F > 0 ? (HitTime_Z < HitTime_F ? HitTime_Z : HitTime_F) : HitTime_Z) : HitTime_F);   
			HitPoint = Target_Position + Velocity*HitTime + 0.5*Acceleration*HitTime*HitTime;   
		}   
		//迭代，仅迭代更新碰撞时间，每次迭代更新右5位数量级   
		for(int i = 0; i < 3; i ++)   
		{   
			if(AccDistance < Vector3D.Distance(HitPoint,Me_Position))//目标在炮弹加速过程外   
			{   
				HitTime = (Vector3D.Distance(HitPoint,Me_Position) - Bullet_InitialSpeed*AccTime - 0.5*Bullet_Acceleration*AccTime*AccTime + Bullet_MaxSpeed*AccTime)/Bullet_MaxSpeed;   
				HitPoint = Target_Position + Velocity*HitTime + 0.5*Acceleration*HitTime*HitTime;   
			}   
			else//目标在炮弹加速过程内   
			{   
				double HitTime_Z = (-Bullet_InitialSpeed + Math.Pow((Bullet_InitialSpeed*Bullet_InitialSpeed + 2*Bullet_Acceleration*Vector3D.Distance(HitPoint,Me_Position)),0.5))/Bullet_Acceleration;   
				double HitTime_F = (-Bullet_InitialSpeed - Math.Pow((Bullet_InitialSpeed*Bullet_InitialSpeed + 2*Bullet_Acceleration*Vector3D.Distance(HitPoint,Me_Position)),0.5))/Bullet_Acceleration;   
				HitTime = (HitTime_Z > 0 ? (HitTime_F > 0 ? (HitTime_Z < HitTime_F ? HitTime_Z : HitTime_F) : HitTime_Z) : HitTime_F);   
				HitPoint = Target_Position + Velocity*HitTime + 0.5*Acceleration*HitTime*HitTime;   
			}   
		}   
		return HitPoint;   
	}

	// -- 计算可扫描的摄像头 --
	public static List<IMyCameraBlock> GetCanScanCameras(List<IMyCameraBlock> Cams, Vector3D Point)
	{
		List<IMyCameraBlock> res = new List<IMyCameraBlock>();
		foreach(IMyCameraBlock cm in Cams){
			if(cm.IsFunctional && cm.CanScan(Point)){
				res.Add(cm);
			}
		}
		return res;
	}
	// -- 计算某向量的垂直向量 --
	// 传入一个向量，和一个点，返回沿这个点出发与传入向量垂直的归一化向量
	public static Vector3D CaculateVerticalVector(Vector3D Vector, Vector3D Point)
	{
		double x = 1;
		double y = 1;
		double z = (Point.X*Vector.X + Point.Y*Vector.Y + Point.Z*Vector.Z)/Vector.Z;
		return Vector3D.Normalize(new Vector3D(x,y,z));
	}
}