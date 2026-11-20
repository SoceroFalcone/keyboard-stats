---
name: "键盘统计器"
description: "Native Windows keyboard and mouse statistics, with the user-selected Meridian Blue theme."
colors:
  sidebar: "#E2E5EA"
  ground: "#EEF0F3"
  surface: "#FFFFFF"
  ink: "#181B21"
  muted: "#5A6072"
  primary: "#3B608F"
  line: "#C9CFD8"
  nav-selected: "#D8E2EE"
  nav-ink: "#1F3A5C"
  hover-line: "#6B87B0"
  heat-zero: "#EEF0F3"
  heat-low: "#D8E2EE"
  heat-medium: "#A8C2DD"
  heat-high: "#3B608F"
  heat-peak: "#1F3A5C"
  selection: "#B8694A"
  notice-ground: "#FFF1D5"
  notice-ink: "#805B1F"
typography:
  family: "Segoe UI, Microsoft YaHei UI"
  body: "14px"
  caption: "13px"
  peripheral: "12px"
  section: "17px"
  headline: "25px"
  metric: "30px"
rounded:
  control: "7px"
  navigation: "8px"
  panel: "12px"
spacing:
  micro: "4px"
  inset: "12px"
  gap: "16px"
  panel: "22px"
  workspace: "28px"
---

# 键盘统计器 1.6 设计规范

本版采用用户明确选定的 [Meridian Blue](https://21st.dev/community/themes/meridian-blue)，替代此前的松绿色界面及热力颜色。21st 提供主题与布局参考；应用仍使用现有原生 WPF 实现。原始主题 CSS 保存在 `artifacts/ui-refinement/reference/meridian-blue.css`，上一版规范在同目录的 `design-before-1.6.md` 留档。

## 配色与数据层

浅灰侧栏和页面承托白色工作面，低饱和蓝用于当前选择与操作。五级蓝色热力表示相对使用频率，颜色越深次数越多；零值有独立的最浅级。键盘与鼠标各自按自身的最大次数归一化。选中部位使用主题中的暖铜色描边，键盘焦点使用白色与深蓝双色轮廓；频率、选择和焦点各自独立。

普通文字为深灰，辅助文字为灰蓝。原生按钮悬停和按下不淡化文字，避免损失对比度。历史热力图将区间内不同的非零次数按大小映射到六档蓝色，相同次数同色。无记录日期显示灰色。日期悬停卡片显示准确次数及较前一天的变化。

## 字号与空间

所有尺寸为 WPF 设备独立像素（DIP），随 Windows 缩放。正文、按钮、日期输入与表格为 14 DIP；说明为 13 DIP；外围标记为 12 DIP；区块、页面标题和主要数字分别为 17、25、30 DIP。保留原生中文字体回退，不加载在线字体。键帽文字根据物理轮廓和可用宽度拟合，实际贴图下最低对比度为 5.65:1。

侧栏 176 DIP，主区域左右边距 28 DIP。概览标题、日期和导出同排，四个摘要并列。键盘和鼠标按可用高度一起缩放，小时分布与常用键排行并排显示。历史页的设备筛选单独一行。原生日历日期按钮为 14 DIP，最小 32 × 30 DIP。

## 模型与性能

沿用照片式无数字区键盘与竖置鼠标；鼠标两个侧键保留在物理左侧。键盘 87 个原生按键目标和硬件 Fn 外观不变，额外数字区按键在“全部按键”中查看。图片、轮廓、点击区域和材质算法保持原样，仅替换热力颜色。

模型使用静态底图与按需生成的小块材质，每个部位只保留当前热度贴图；同级计数更新复用贴图。托盘启动不建立窗口，关闭后约 8 秒释放界面，最小化停止刷新。没有网页运行环境、持续动画或新的采集计时器。95 个图片来源均通过退役后的释放检查。

## 日期与历史记录

概览有前一天、后一天、日历及今天／昨天快捷选择。清空或无效日期不会留下日期与数据不一致的状态。历史顶部切换全部／键盘／鼠标，同时更新活动热力图和每日记录。

活动热力图可查看近 7 天、近 30 天、近 180 天或近一年（365 天），均包含今天。每周从周一到周日排成一列，当前周在最右侧。每日记录可查看全部日期、今天、昨天、近 7 天、近 30 天和自定义范围。范围包含起止日，自定义范围无效时清除结果展示并禁用导出。历史 CSV 跟随每日记录的日期与设备筛选，设置中仍能导出全部记录。活跃分钟明确标为键鼠合计。

表格按可见区域限制高度并复用行，筛选条件与列标题在滚动到每日记录时一起保持可见。历史与设置分别保留滚动位置。概览在最小窗口尺寸内显示全部区块，「全部按键」使用可搜索的分页浮层。面板释放后再打开，设备及日期范围选择会恢复。

## 验证

查看 `docs/visual-review-v1.6.md`、`docs/TESTING.md`、`docs/PERFORMANCE.md`。最终实际 WPF 渲染图位于 `artifacts/ui-refinement/preview-final`，涵盖 1280 × 900 与 1060 × 700 DIP、选择／焦点、各种热度、日期弹层、筛选、空结果与无效输入。截图均使用标明的独立演示数据。
