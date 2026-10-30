using MajdataViewX.Base;
using MajdataViewX.Utils;
using MajdataViewX.Utils.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using static MajdataViewX.Base.MajCtx;
// ReSharper disable InconsistentNaming

namespace MajdataViewX.Managers
{
    public class SkinManager : MonoBehaviour
    {
        // ============ Note Skin ============
        public static readonly int COUNT = Enum.GetValues(typeof(NoteSp)).Length;

        public enum NoteSp : uint
        {
            TAP,
            TAP_EACH,
            TAP_BREAK,
            TAP_EX,
            TAP_MINE,
            TAP_BREAK_MINE,

            SLIDE,
            SLIDE_EACH,
            SLIDE_BREAK,
            SLIDE_MINE,
            SLIDE_BREAK_MINE,

            WIFI_0,
            WIFI_1,
            WIFI_2,
            WIFI_3,
            WIFI_4,
            WIFI_5,
            WIFI_6,
            WIFI_7,
            WIFI_8,
            WIFI_9,
            WIFI_10,
            WIFI_EACH_0,
            WIFI_EACH_1,
            WIFI_EACH_2,
            WIFI_EACH_3,
            WIFI_EACH_4,
            WIFI_EACH_5,
            WIFI_EACH_6,
            WIFI_EACH_7,
            WIFI_EACH_8,
            WIFI_EACH_9,
            WIFI_EACH_10,
            WIFI_BREAK_0,
            WIFI_BREAK_1,
            WIFI_BREAK_2,
            WIFI_BREAK_3,
            WIFI_BREAK_4,
            WIFI_BREAK_5,
            WIFI_BREAK_6,
            WIFI_BREAK_7,
            WIFI_BREAK_8,
            WIFI_BREAK_9,
            WIFI_BREAK_10,
            WIFI_MINE_0,
            WIFI_MINE_1,
            WIFI_MINE_2,
            WIFI_MINE_3,
            WIFI_MINE_4,
            WIFI_MINE_5,
            WIFI_MINE_6,
            WIFI_MINE_7,
            WIFI_MINE_8,
            WIFI_MINE_9,
            WIFI_MINE_10,
            WIFI_BREAK_MINE_0,
            WIFI_BREAK_MINE_1,
            WIFI_BREAK_MINE_2,
            WIFI_BREAK_MINE_3,
            WIFI_BREAK_MINE_4,
            WIFI_BREAK_MINE_5,
            WIFI_BREAK_MINE_6,
            WIFI_BREAK_MINE_7,
            WIFI_BREAK_MINE_8,
            WIFI_BREAK_MINE_9,
            WIFI_BREAK_MINE_10,

            STAR,
            STAR_DOUBLE,
            STAR_EACH,
            STAR_EACH_DOUBLE,
            STAR_BREAK,
            STAR_BREAK_DOUBLE,
            STAR_MINE,
            STAR_MINE_DOUBLE,
            STAR_EX,
            STAR_EX_DOUBLE,
            STAR_BREAK_MINE,
            STAR_BREAK_DOUBLE_MINE,

            HOLD,
            HOLD_ON,
            HOLD_OFF,
            HOLD_EACH,
            HOLD_EACH_ON,
            HOLD_BREAK,
            HOLD_BREAK_ON,
            HOLD_MINE,
            HOLD_MINE_ON,
            HOLD_BREAK_MINE,
            HOLD_BREAK_MINE_ON,
            HOLD_EX,

            JUST_STR_L,
            JUST_STR_R,
            JUST_CURV_L,
            JUST_CURV_R,
            JUST_WIFI_U,
            JUST_WIFI_D,

            JUST_STR_L_FAST_GR,
            JUST_STR_R_FAST_GR,
            JUST_CURV_L_FAST_GR,
            JUST_CURV_R_FAST_GR,
            JUST_WIFI_U_FAST_GR,
            JUST_WIFI_D_FAST_GR,

            JUST_STR_L_FAST_GD,
            JUST_STR_R_FAST_GD,
            JUST_CURV_L_FAST_GD,
            JUST_CURV_R_FAST_GD,
            JUST_WIFI_U_FAST_GD,
            JUST_WIFI_D_FAST_GD,

            JUST_STR_L_LATE_GR,
            JUST_STR_R_LATE_GR,
            JUST_CURV_L_LATE_GR,
            JUST_CURV_R_LATE_GR,
            JUST_WIFI_U_LATE_GR,
            JUST_WIFI_D_LATE_GR,

            JUST_STR_L_LATE_GD,
            JUST_STR_R_LATE_GD,
            JUST_CURV_L_LATE_GD,
            JUST_CURV_R_LATE_GD,
            JUST_WIFI_U_LATE_GD,
            JUST_WIFI_D_LATE_GD,

            JUST_STR_L_MISS,
            JUST_STR_R_MISS,
            JUST_CURV_L_MISS,
            JUST_CURV_R_MISS,
            JUST_WIFI_U_MISS,
            JUST_WIFI_D_MISS,

            JUST_STR_L_TOOFAST,
            JUST_STR_R_TOOFAST,
            JUST_CURV_L_TOOFAST,
            JUST_CURV_R_TOOFAST,
            JUST_WIFI_U_TOOFAST,
            JUST_WIFI_D_TOOFAST,

            JUST_STR_L_BREAK,
            JUST_STR_R_BREAK,
            JUST_CURV_L_BREAK,
            JUST_CURV_R_BREAK,
            JUST_WIFI_U_BREAK,
            JUST_WIFI_D_BREAK,

            JUDGE_TEXT_0,
            JUDGE_TEXT_1,
            JUDGE_TEXT_2,
            JUDGE_TEXT_3,
            JUDGE_TEXT_4,
            JUDGE_TEXT_BREAK,

            FAST_TEXT,
            LATE_TEXT,

            TOUCH,
            TOUCH_EACH,
            TOUCH_BREAK,
            TOUCH_MINE,
            TOUCH_BREAK_MINE,

            TOUCH_POINT,
            TOUCH_POINT_EACH,
            TOUCH_POINT_BREAK,
            TOUCH_POINT_MINE,
            TOUCH_POINT_BREAK_MINE,

            TOUCH_JUST,

            TOUCH_BORDER_0,
            TOUCH_BORDER_1,
            TOUCH_BORDER_EACH_0,
            TOUCH_BORDER_EACH_1,
            TOUCH_BORDER_BREAK_0,
            TOUCH_BORDER_BREAK_1,
            TOUCH_BORDER_MINE_0,
            TOUCH_BORDER_MINE_1,
            TOUCH_BORDER_BREAK_MINE_0,
            TOUCH_BORDER_BREAK_MINE_1,

            TOUCH_HOLD_0,
            TOUCH_HOLD_1,
            TOUCH_HOLD_2,
            TOUCH_HOLD_3,
            TOUCH_HOLD_BREAK_0,
            TOUCH_HOLD_BREAK_1,
            TOUCH_HOLD_BREAK_2,
            TOUCH_HOLD_BREAK_3,
            TOUCH_HOLD_MINE_0,
            TOUCH_HOLD_MINE_1,
            TOUCH_HOLD_MINE_2,
            TOUCH_HOLD_MINE_3,

            TOUCH_HOLD_BORDER,
            TOUCH_HOLD_BORDER_BREAK,
            TOUCH_HOLD_BORDER_MINE,
            TOUCH_HOLD_BORDER_BREAK_MINE,
            TOUCH_HOLD_BORDER_MISS,

            LINE,
            LINE_EACH,
            LINE_MINE,
            LINE_BREAK,
            LINE_STAR,

            EACH_LINE_0,
            EACH_LINE_1,
            EACH_LINE_2,
            EACH_LINE_3,

            HOLD_END,
            HOLD_END_EACH,
            HOLD_END_BREAK,
            HOLD_END_MINE,
        }



        public static readonly float4 Ex = new(1f, 0.7176471f, 0.9098039f, 1f);
        public static readonly float4 Ex_Each = new(1f, 0.9607843f, 0.3647059f, 1f);
        public static readonly float4 Ex_Star = new(0f, 0.8f, 1f, 1f);
        public static readonly float4 Ex_Break = new(1f, 0.74509805f, 0.3137255f, 1f);
        public static readonly float4 Ex_Mine = new(0.15294117f, 0.15294117f, 0.15294117f, 1f);

        public const float HoldBaseWidth = 1.22f;              // legacy spriteRenderer.size.x
        public const float HoldCapAllowance = 1.4f;            // legacy total sprite height
        public const float HoldCapEach = 58 / 100f;            // 58px / 100PPU
        public const float HoldNativeWidth = 122 / 100f;       // tex.width / 100
        public const float HoldNativeHeight = 200 / 100f;      // tex.height / 100
        public static readonly float2 HoldSliceBorder = new(HoldCapEach / HoldNativeHeight); // capWorld / nativeHeight

        public Texture2D Atlas;
        public NativeArray<float4> Uvs;

        // =========== Other Skin ============

        public Sprite[] JudgeText = new Sprite[5];
        public Sprite JudgeText_BPerfect;
        public Sprite FastText;
        public Sprite LateText;

        public Sprite[] TouchBorder_Normal = new Sprite[2];
        public Sprite[] TouchBorder_Each = new Sprite[2];
        public Sprite[] TouchBorder_Break = new Sprite[2];
        public Sprite[] TouchBorder_Mine = new Sprite[2];
        public Sprite[] TouchBorder_Break_Mine = new Sprite[2];

        public Sprite Outline;

        private void Awake()
        {
            _noteSkinManager = this;

            var skinPath = MajEnv.GetPath("Skin");
            var tapPath = Path.Combine(skinPath, "TapSkins");
            var slidePath = Path.Combine(skinPath, "SlideSkins");
            var wifiPath = Path.Combine(skinPath, "WifiSkins");
            var starPath = Path.Combine(skinPath, "StarSkins");
            var holdPath = Path.Combine(skinPath, "HoldSkins");
            var slideOkPath = Path.Combine(skinPath, "SlideOKSkins");
            var judgeTextPath = Path.Combine(skinPath, "JudgeTextSkins");
            var touchPath = Path.Combine(skinPath, "TouchSkins");
            var touchHoldPath = Path.Combine(skinPath, "TouchHoldSkins");
            var noteGuidePath = Path.Combine(skinPath, "NoteGuideSkins");

            var sources = new List<(string path, int index, Texture2D tex)>(COUNT);

            Add(sources, NoteSp.TAP, tapPath + "/tap.png");
            Add(sources, NoteSp.TAP_EACH, tapPath + "/tap_each.png");
            Add(sources, NoteSp.TAP_BREAK, tapPath + "/tap_break.png");
            Add(sources, NoteSp.TAP_EX, tapPath + "/tap_ex.png");
            Add(sources, NoteSp.TAP_MINE, tapPath + "/tap_mine.png");
            Add(sources, NoteSp.TAP_BREAK_MINE, tapPath + "/tap_break_mine.png");

            Add(sources, NoteSp.SLIDE, slidePath + "/slide.png");
            Add(sources, NoteSp.SLIDE_EACH, slidePath + "/slide_each.png");
            Add(sources, NoteSp.SLIDE_BREAK, slidePath + "/slide_break.png");
            Add(sources, NoteSp.SLIDE_MINE, slidePath + "/slide_mine.png");
            Add(sources, NoteSp.SLIDE_BREAK_MINE, slidePath + "/slide_break_mine.png");

            for (int i = 0; i < 11; i++)
            {
                Add(sources, NoteSp.WIFI_0.Offset(i), wifiPath + "/wifi_" + i + ".png");
                Add(sources, NoteSp.WIFI_EACH_0.Offset(i), wifiPath + "/wifi_each_" + i + ".png");
                Add(sources, NoteSp.WIFI_BREAK_0.Offset(i), wifiPath + "/wifi_break_" + i + ".png");
                Add(sources, NoteSp.WIFI_MINE_0.Offset(i), wifiPath + "/wifi_mine_" + i + ".png");
                Add(sources, NoteSp.WIFI_BREAK_MINE_0.Offset(i), wifiPath + "/wifi_break_mine_" + i + ".png");
            }

            Add(sources, NoteSp.STAR, starPath + "/star.png");
            Add(sources, NoteSp.STAR_DOUBLE, starPath + "/star_double.png");
            Add(sources, NoteSp.STAR_EACH, starPath + "/star_each.png");
            Add(sources, NoteSp.STAR_EACH_DOUBLE, starPath + "/star_each_double.png");
            Add(sources, NoteSp.STAR_BREAK, starPath + "/star_break.png");
            Add(sources, NoteSp.STAR_BREAK_DOUBLE, starPath + "/star_break_double.png");
            Add(sources, NoteSp.STAR_MINE, starPath + "/star_mine.png");
            Add(sources, NoteSp.STAR_MINE_DOUBLE, starPath + "/star_double_mine.png");
            Add(sources, NoteSp.STAR_EX, starPath + "/star_ex.png");
            Add(sources, NoteSp.STAR_EX_DOUBLE, starPath + "/star_ex_double.png");
            Add(sources, NoteSp.STAR_BREAK_MINE, starPath + "/star_break_mine.png");
            Add(sources, NoteSp.STAR_BREAK_DOUBLE_MINE, starPath + "/star_break_double_mine.png");

            Add(sources, NoteSp.HOLD, holdPath + "/hold.png");
            Add(sources, NoteSp.HOLD_EACH, holdPath + "/hold_each.png");
            Add(sources, NoteSp.HOLD_BREAK, holdPath + "/hold_break.png");
            Add(sources, NoteSp.HOLD_MINE, holdPath + "/hold_mine.png");
            Add(sources, NoteSp.HOLD_BREAK_MINE, holdPath + "/hold_break_mine.png");
            Add(sources, NoteSp.HOLD_EX, holdPath + "/hold_ex.png");
            Add(sources, NoteSp.HOLD_OFF, holdPath + "/hold_off.png");
            Add(sources, NoteSp.HOLD_ON, File.Exists(holdPath + "/hold_on.png") ? holdPath + "/hold_on.png" : holdPath + "/hold.png");
            Add(sources, NoteSp.HOLD_EACH_ON, File.Exists(holdPath + "/hold_each_on.png") ? holdPath + "/hold_each_on.png" : holdPath + "/hold_each.png");
            Add(sources, NoteSp.HOLD_BREAK_ON, File.Exists(holdPath + "/hold_break_on.png") ? holdPath + "/hold_break_on.png" : holdPath + "/hold_break.png");
            Add(sources, NoteSp.HOLD_MINE_ON, File.Exists(holdPath + "/hold_mine_on.png") ? holdPath + "/hold_mine_on.png" : holdPath + "/hold_mine.png");
            Add(sources, NoteSp.HOLD_BREAK_MINE_ON, File.Exists(holdPath + "/hold_break_mine_on.png") ? holdPath + "/hold_break_mine_on.png" : holdPath + "/hold_break_mine.png");

            Add(sources, NoteSp.JUST_STR_L, slideOkPath + "/just_str_l.png");
            Add(sources, NoteSp.JUST_STR_R, slideOkPath + "/just_str_r.png");
            Add(sources, NoteSp.JUST_CURV_L, slideOkPath + "/just_curv_l.png");
            Add(sources, NoteSp.JUST_CURV_R, slideOkPath + "/just_curv_r.png");
            Add(sources, NoteSp.JUST_WIFI_U, slideOkPath + "/just_wifi_u.png");
            Add(sources, NoteSp.JUST_WIFI_D, slideOkPath + "/just_wifi_d.png");

            Add(sources, NoteSp.JUST_STR_L_FAST_GR, slideOkPath + "/just_str_l_fast_gr.png");
            Add(sources, NoteSp.JUST_STR_R_FAST_GR, slideOkPath + "/just_str_r_fast_gr.png");
            Add(sources, NoteSp.JUST_CURV_L_FAST_GR, slideOkPath + "/just_curv_l_fast_gr.png");
            Add(sources, NoteSp.JUST_CURV_R_FAST_GR, slideOkPath + "/just_curv_r_fast_gr.png");
            Add(sources, NoteSp.JUST_WIFI_U_FAST_GR, slideOkPath + "/just_wifi_u_fast_gr.png");
            Add(sources, NoteSp.JUST_WIFI_D_FAST_GR, slideOkPath + "/just_wifi_d_fast_gr.png");

            Add(sources, NoteSp.JUST_STR_L_FAST_GD, slideOkPath + "/just_str_l_fast_gd.png");
            Add(sources, NoteSp.JUST_STR_R_FAST_GD, slideOkPath + "/just_str_r_fast_gd.png");
            Add(sources, NoteSp.JUST_CURV_L_FAST_GD, slideOkPath + "/just_curv_l_fast_gd.png");
            Add(sources, NoteSp.JUST_CURV_R_FAST_GD, slideOkPath + "/just_curv_r_fast_gd.png");
            Add(sources, NoteSp.JUST_WIFI_U_FAST_GD, slideOkPath + "/just_wifi_u_fast_gd.png");
            Add(sources, NoteSp.JUST_WIFI_D_FAST_GD, slideOkPath + "/just_wifi_d_fast_gd.png");

            Add(sources, NoteSp.JUST_STR_L_LATE_GR, slideOkPath + "/just_str_l_late_gr.png");
            Add(sources, NoteSp.JUST_STR_R_LATE_GR, slideOkPath + "/just_str_r_late_gr.png");
            Add(sources, NoteSp.JUST_CURV_L_LATE_GR, slideOkPath + "/just_curv_l_late_gr.png");
            Add(sources, NoteSp.JUST_CURV_R_LATE_GR, slideOkPath + "/just_curv_r_late_gr.png");
            Add(sources, NoteSp.JUST_WIFI_U_LATE_GR, slideOkPath + "/just_wifi_u_late_gr.png");
            Add(sources, NoteSp.JUST_WIFI_D_LATE_GR, slideOkPath + "/just_wifi_d_late_gr.png");

            Add(sources, NoteSp.JUST_STR_L_LATE_GD, slideOkPath + "/just_str_l_late_gd.png");
            Add(sources, NoteSp.JUST_STR_R_LATE_GD, slideOkPath + "/just_str_r_late_gd.png");
            Add(sources, NoteSp.JUST_CURV_L_LATE_GD, slideOkPath + "/just_curv_l_late_gd.png");
            Add(sources, NoteSp.JUST_CURV_R_LATE_GD, slideOkPath + "/just_curv_r_late_gd.png");
            Add(sources, NoteSp.JUST_WIFI_U_LATE_GD, slideOkPath + "/just_wifi_u_late_gd.png");
            Add(sources, NoteSp.JUST_WIFI_D_LATE_GD, slideOkPath + "/just_wifi_d_late_gd.png");

            Add(sources, NoteSp.JUST_STR_L_MISS, slideOkPath + "/miss_str_l.png");
            Add(sources, NoteSp.JUST_STR_R_MISS, slideOkPath + "/miss_str_r.png");
            Add(sources, NoteSp.JUST_CURV_L_MISS, slideOkPath + "/miss_curv_l.png");
            Add(sources, NoteSp.JUST_CURV_R_MISS, slideOkPath + "/miss_curv_r.png");
            Add(sources, NoteSp.JUST_WIFI_U_MISS, slideOkPath + "/miss_wifi_u.png");
            Add(sources, NoteSp.JUST_WIFI_D_MISS, slideOkPath + "/miss_wifi_d.png");

            Add(sources, NoteSp.JUST_STR_L_TOOFAST, slideOkPath + "/toofast_str_l.png");
            Add(sources, NoteSp.JUST_STR_R_TOOFAST, slideOkPath + "/toofast_str_r.png");
            Add(sources, NoteSp.JUST_CURV_L_TOOFAST, slideOkPath + "/toofast_curv_l.png");
            Add(sources, NoteSp.JUST_CURV_R_TOOFAST, slideOkPath + "/toofast_curv_r.png");
            Add(sources, NoteSp.JUST_WIFI_U_TOOFAST, slideOkPath + "/toofast_wifi_u.png");
            Add(sources, NoteSp.JUST_WIFI_D_TOOFAST, slideOkPath + "/toofast_wifi_d.png");

            Add(sources, NoteSp.JUST_STR_L_BREAK, slideOkPath + "/just_str_l_break.png");
            Add(sources, NoteSp.JUST_STR_R_BREAK, slideOkPath + "/just_str_r_break.png");
            Add(sources, NoteSp.JUST_CURV_L_BREAK, slideOkPath + "/just_curv_l_break.png");
            Add(sources, NoteSp.JUST_CURV_R_BREAK, slideOkPath + "/just_curv_r_break.png");
            Add(sources, NoteSp.JUST_WIFI_U_BREAK, slideOkPath + "/just_wifi_u_break.png");
            Add(sources, NoteSp.JUST_WIFI_D_BREAK, slideOkPath + "/just_wifi_d_break.png");

            Add(sources, NoteSp.JUDGE_TEXT_0, judgeTextPath + "/judge_text_miss.png");
            Add(sources, NoteSp.JUDGE_TEXT_1, judgeTextPath + "/judge_text_good.png");
            Add(sources, NoteSp.JUDGE_TEXT_2, judgeTextPath + "/judge_text_great.png");
            Add(sources, NoteSp.JUDGE_TEXT_3, judgeTextPath + "/judge_text_perfect.png");
            Add(sources, NoteSp.JUDGE_TEXT_4, judgeTextPath + "/judge_text_cPerfect.png");
            Add(sources, NoteSp.JUDGE_TEXT_BREAK, judgeTextPath + "/judge_text_break.png");

            Add(sources, NoteSp.FAST_TEXT, judgeTextPath + "/fast.png");
            Add(sources, NoteSp.LATE_TEXT, judgeTextPath + "/late.png");

            Add(sources, NoteSp.TOUCH, touchPath + "/touch.png");
            Add(sources, NoteSp.TOUCH_EACH, touchPath + "/touch_each.png");
            Add(sources, NoteSp.TOUCH_BREAK, touchPath + "/touch_break.png");
            Add(sources, NoteSp.TOUCH_MINE, touchPath + "/touch_mine.png");
            Add(sources, NoteSp.TOUCH_BREAK_MINE, touchPath + "/touch_break_mine.png");

            Add(sources, NoteSp.TOUCH_POINT, touchPath + "/touch_point.png");
            Add(sources, NoteSp.TOUCH_POINT_EACH, touchPath + "/touch_point_each.png");
            Add(sources, NoteSp.TOUCH_POINT_BREAK, touchPath + "/touch_break_point.png");
            Add(sources, NoteSp.TOUCH_POINT_MINE, touchPath + "/touch_point_mine.png");
            Add(sources, NoteSp.TOUCH_POINT_BREAK_MINE, touchPath + "/touch_break_point_mine.png");

            Add(sources, NoteSp.TOUCH_JUST, touchPath + "/touch_just.png");

            Add(sources, NoteSp.TOUCH_BORDER_0, touchPath + "/touch_border_2.png");
            Add(sources, NoteSp.TOUCH_BORDER_1, touchPath + "/touch_border_3.png");
            Add(sources, NoteSp.TOUCH_BORDER_EACH_0, touchPath + "/touch_border_2_each.png");
            Add(sources, NoteSp.TOUCH_BORDER_EACH_1, touchPath + "/touch_border_3_each.png");
            Add(sources, NoteSp.TOUCH_BORDER_BREAK_0, touchPath + "/touch_break_border_2.png");
            Add(sources, NoteSp.TOUCH_BORDER_BREAK_1, touchPath + "/touch_break_border_3.png");
            Add(sources, NoteSp.TOUCH_BORDER_MINE_0, touchPath + "/touch_mine_border_2.png");
            Add(sources, NoteSp.TOUCH_BORDER_MINE_1, touchPath + "/touch_mine_border_3.png");
            Add(sources, NoteSp.TOUCH_BORDER_BREAK_MINE_0, touchPath + "/touch_break_mine_border_2.png");
            Add(sources, NoteSp.TOUCH_BORDER_BREAK_MINE_1, touchPath + "/touch_break_mine_border_3.png");

            for (int i = 0; i < 4; i++)
            {
                Add(sources, NoteSp.TOUCH_HOLD_0.Offset(i), touchHoldPath + "/touchhold_" + i + ".png");
                Add(sources, NoteSp.TOUCH_HOLD_BREAK_0.Offset(i), touchHoldPath + "/touchhold_break_" + i + ".png");
                Add(sources, NoteSp.TOUCH_HOLD_MINE_0.Offset(i), touchHoldPath + "/touchhold_mine_" + i + ".png");
            }
            Add(sources, NoteSp.TOUCH_HOLD_BORDER, touchHoldPath + "/touchhold_border.png");
            Add(sources, NoteSp.TOUCH_HOLD_BORDER_BREAK, touchHoldPath + "/touchhold_break_border.png");
            Add(sources, NoteSp.TOUCH_HOLD_BORDER_BREAK_MINE, touchHoldPath + "/touchhold_break_mine.png");
            Add(sources, NoteSp.TOUCH_HOLD_BORDER_MINE, touchHoldPath + "/touchhold_mine.png");
            Add(sources, NoteSp.TOUCH_HOLD_BORDER_MISS, touchHoldPath + "/touchhold_off.png");

            Add(sources, NoteSp.LINE, noteGuidePath + "/Normal.png");
            Add(sources, NoteSp.LINE_EACH, noteGuidePath + "/Each.png");
            Add(sources, NoteSp.LINE_BREAK, noteGuidePath + "/Break.png");
            Add(sources, NoteSp.LINE_STAR, noteGuidePath + "/Slide.png");
            Add(sources, NoteSp.LINE_MINE, noteGuidePath + "/Mine.png");

            for (int i = 0; i < 4; i++)
                Add(sources, NoteSp.EACH_LINE_0.Offset(i), noteGuidePath + "/EachLine" + (i + 1) + ".png");

            Add(sources, NoteSp.HOLD_END, noteGuidePath + "/Hold_End.png");
            Add(sources, NoteSp.HOLD_END_EACH, noteGuidePath + "/Hold_Each_End.png");
            Add(sources, NoteSp.HOLD_END_BREAK, noteGuidePath + "/Hold_Break_End.png");
            Add(sources, NoteSp.HOLD_END_MINE, noteGuidePath + "/Hold_Mine_End.png");

            // Load judge sprites separately for EffectManager (atlas textures get destroyed)
            JudgeText[0] = TexLoader.LoadSprite(judgeTextPath + "/judge_text_miss.png");
            JudgeText[1] = TexLoader.LoadSprite(judgeTextPath + "/judge_text_good.png");
            JudgeText[2] = TexLoader.LoadSprite(judgeTextPath + "/judge_text_great.png");
            JudgeText[3] = TexLoader.LoadSprite(judgeTextPath + "/judge_text_perfect.png");
            JudgeText[4] = TexLoader.LoadSprite(judgeTextPath + "/judge_text_cPerfect.png");
            JudgeText_BPerfect = TexLoader.LoadSprite(judgeTextPath + "/judge_text_cPerfect_break.png");
            FastText = TexLoader.LoadSprite(judgeTextPath + "/fast.png");
            LateText = TexLoader.LoadSprite(judgeTextPath + "/late.png");

            TouchBorder_Normal[0] = TexLoader.LoadSprite(touchPath + "/TouchSkins/touch_border_2.png");
            TouchBorder_Normal[1] = TexLoader.LoadSprite(touchPath + "/TouchSkins/touch_border_3.png");
            TouchBorder_Each[0] = TexLoader.LoadSprite(touchPath + "/TouchSkins/touch_border_2_each.png");
            TouchBorder_Each[1] = TexLoader.LoadSprite(touchPath + "/TouchSkins/touch_border_3_each.png");
            TouchBorder_Break[0] = TexLoader.LoadSprite(touchPath + "/TouchSkins/touch_break_border_2.png");
            TouchBorder_Break[1] = TexLoader.LoadSprite(touchPath + "/TouchSkins/touch_break_border_3.png");
            TouchBorder_Mine[0] = TexLoader.LoadSprite(touchPath + "/TouchSkins/touch_mine_border_2.png");
            TouchBorder_Mine[1] = TexLoader.LoadSprite(touchPath + "/TouchSkins/touch_mine_border_3_mine.png");
            TouchBorder_Break_Mine[0] = TexLoader.LoadSprite(touchPath + "/TouchSkins/touch_break_mine_border_2.png");
            TouchBorder_Break_Mine[1] = TexLoader.LoadSprite(touchPath + "/TouchSkins/touch_break_mine_border_3.png");

            Outline = TexLoader.LoadSprite(Path.Combine(skinPath, "outline.png"));

            BuildAtlas(sources);
        }

        private void Start()
        {
            GetComponent<SpriteRenderer>().sprite = Outline;
        }

        private void Add(List<(string path, int index, Texture2D tex)> list, NoteSp index, string path)
        {
            var tex = TexLoader.LoadTexture(path);
            list.Add((path, (int)index, tex));
        }



        private void BuildAtlas(List<(string path, int index, Texture2D tex)> sources)
        {
            const int maxAtlasSize = 8192;

            if (Uvs.IsCreated) Uvs.Dispose();
            if (Atlas != null) Destroy(Atlas);
            Uvs = new NativeArray<float4>(COUNT, Allocator.Persistent);
            Atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            var textures = new Texture2D[sources.Count];
            for (var i = 0; i < sources.Count; i++)
                textures[i] = sources[i].tex;

            try
            {
                var rects = Atlas.PackTextures(
                    textures,
                    padding: 0,
                    maximumAtlasSize: maxAtlasSize,
                    makeNoLongerReadable: true);
                if (rects == null || rects.Length != sources.Count)
                    throw new InvalidOperationException(
                        $"Could not pack all skin textures into a {maxAtlasSize}x{maxAtlasSize} atlas.");

                var halfTexelX = 0.5f / Atlas.width;
                var halfTexelY = 0.5f / Atlas.height;
                for (var i = 0; i < sources.Count; i++)
                {
                    var (_, index, _) = sources[i];
                    var rect = rects[i];
                    Uvs[index] = new float4(
                        rect.xMin + halfTexelX,
                        rect.yMin + halfTexelY,
                        rect.xMax - halfTexelX,
                        rect.yMax - halfTexelY);
                }
            }
            finally
            {
                foreach (var texture in textures)
                    if (texture != null)
                        Destroy(texture);
            }
        }

        private void OnDestroy()
        {
            if (Uvs.IsCreated) Uvs.Dispose();
            if (Atlas != null) Destroy(Atlas);
        }
    }
}