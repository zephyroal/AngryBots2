/// <summary>
/// 敌人种类。用于计分、刷怪表、击杀事件与难度缩放。
/// 与旧 <c>EnemyHealth</c> / <c>EnemyAttack</c> 体系并存：旧体系不认识敌人种类，
/// 由新体系（<c>EnemyDefinition</c> + <c>EnemyRegistry</c>）补上这一层元数据。
/// </summary>
public enum EnemyKind
{
	/// <summary>爬行蜘蛛，近战，原始 AB2 敌人。</summary>
	Spider = 0,

	/// <summary>重型机甲，远程扫射 + 冲撞，有护甲减伤。</summary>
	Mech = 1,

	/// <summary>飞行骚扰单位，绕玩家盘旋并打慢速弹。</summary>
	Buzzer = 2,

	/// <summary>自爆蜂，冲刺接近后引爆。</summary>
	Kamikaze = 3,

	/// <summary>Boss（SpiderQueen / MechBoss），多阶段。</summary>
	Boss = 4,
}