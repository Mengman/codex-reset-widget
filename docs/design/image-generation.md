# 设计图生成说明

## 文件与来源

- 输出文件：[windows11-light-dark.png](windows11-light-dark.png)。
- 生成日期：2026 年 10 月 2 日。
- 方式：内置 ImageGen，根据本项目讨论生成一张浅色与深色并排的概念设计图。
- 原图文件名：`exec-048ee9ef-58f0-4f85-a39f-64fea3b50fc9.png`。
- 本次整理直接复制原图，未修改图片。

图中的文字、时间与历史标记是界面示例。它用于布局和视觉参考，不能作为实时数据快照，也不是已经实现的应用截图。

## 生成提示词

以下为生成时使用的完整提示词。

```text
Use case: ui-mockup.
Create a polished high-fidelity visual design image of a Windows 11 desktop widget called "Codex Reset", showing the SAME complete widget side by side in LIGHT and DARK system themes. One comparison design board, not a website, not a phone. Large crisp image approximately 1800x1700. Both widget windows fully visible and equally sized, front-on, no perspective. Comfortable margins around them. Chinese UI with accurate readable Simplified Chinese and some English tweet text.

Visual design: authentically Windows 11 Fluent Design, subtle Mica-like opaque tinted window surface, elegant restrained native application style, Segoe UI Variable typography and Microsoft YaHei-like Chinese glyphs, Fluent thin line icons, 12px outer rounded corners, 8px cards, fine 1px borders, soft elevation shadow, 8px spacing rhythm. Light theme uses warm off-white window, white cards, graphite text, Windows blue #0067C0 accents. Dark theme uses charcoal #202020 window, #2B2B2B cards, #F3F3F3 text, #60CDFF accents. Accessible secondary text, no washed-out labels. No neon, dramatic gradients, excessive glass, macOS traffic lights, decorative 3D objects or generic dashboard charts.

Composition: quiet pale grey-blue background on left half and deep slate-blue on right half with very subtle blurred Windows-style blue wallpaper folds. Small labels above the windows: "浅色模式" on left with sun icon, "深色模式" on right with moon icon. Above both a restrained board title "Codex Reset" and subtitle "Windows 11 桌面小组件". Under both a discreet single caption "跟随系统主题 · 示例数据". Widget itself approximately 420 x 760 logical pixels, enough height for the calendar and footer without cropping. Both versions have EXACT same content, spacing and layout.

Inside each window:
1. Compact native title strip: simple blue circular-arrow app icon, "Codex Reset", right side thin refresh, pin, and ellipsis icons.
2. FIRST FUNCTIONAL REGION: prominent announcement board card. Upper row "下一次重置" left, small subtle blue pill "已预告" right. Large beautiful monospaced/Segoe numeric countdown "13:22:00", visually most prominent content on the entire window. Beneath it "10 月 3 日，01:00" and smaller "北京时间 · 预计全局重置". Small tasteful clock icon. A tiny blue status dot near the supporting line. Keep board sophisticated and calm.
3. SECOND FUNCTIONAL REGION: heading "重置公告". Tweet card with generic circular avatar containing letter T (do not invent a real portrait), author "Tibo", secondary handle "@thsottiaux", small external-link icon at upper right. Tweet in readable English, verbatim: "Global reset landing tomorrow 10am PST for all paid ChatGPT accounts." Fit across 3 lines with natural wrapping. Bottom row timestamp "10 月 2 日 · 10:14" at left, blue text link "查看原文 ↗" at right. No engagement count clutter.
4. THIRD FUNCTIONAL REGION: heading "重置日历". Card header calendar icon and "2026 年 9 月" left, subtle left/right chevrons right. Weekday header "一 二 三 四 五 六 日". True accurate September 2026 calendar, 7 equal columns, 5 rows:
row1: muted 31, 1, 2, 3, 4, 5, 6
row2: 7, 8, 9, 10, 11, 12, 13
row3: 14, 15, 16, 17, 18, 19, 20
row4: 21, 22, 23, 24, 25, 26, 27
row5: 28, 29, 30, muted 1, muted 2, muted 3, muted 4.
Blue tiny dots under dates 8, 12, 26. Small amber diamonds under dates 3, 5, 22, 29. Date 26 selected with rounded Windows-blue filled square and white number in light theme, light blue selection and dark number in dark theme. Beneath calendar small legend "● 全局重置   ◆ 备用重置", dots blue, diamonds amber. Thin divider then compact selected-date detail row: blue circle-arrow icon, "9 月 26 日 · 全局重置", secondary caption "公告已确认完成" with link arrow at right. All fits in this region.
5. Very subtle footer under cards: "数据来自 Codex Resets" with tiny link symbol at left and "2 分钟前更新" at right. No big extra buttons, no bottom navigation.

Priority: balanced proportions, real useful widget density, impeccable alignment, sharp typography, both themes equally legible, three requested functional regions unmistakable. Dates and counts are explicitly example UI content, not live assertions. Produce a finished presentation-quality screenshot-style design image.
```

