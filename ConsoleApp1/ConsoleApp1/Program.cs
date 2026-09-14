using System;
using System.Numerics;
using System.Text;
using Raylib_cs;

class Program
{
    enum InputField { None, PosX, PosY, PosZ, TarY, RotX, RotY, RotZ, CustomHead, CustomBody, CustomFace }
    static InputField activeField = InputField.None;
    static StringBuilder inputBuffer = new StringBuilder();

    enum ColorTarget { None, Face, Body, Rim }
    static ColorTarget activeColorPicker = ColorTarget.None;

    // カラーピッカーグリッドの配置（3Dビュー側に重ねて表示）
    const int ColorGridCols = 9;
    const int ColorGridRows = 7;
    const int ColorGridCell = 30;
    const int ColorGridX = 480;
    const int ColorGridY = 190;

    static void HandleColorPickerClick()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;

        Vector2 m = Raylib.GetMousePosition();
        int col = (int)((m.X - ColorGridX) / ColorGridCell);
        int row = (int)((m.Y - ColorGridY) / ColorGridCell);
        if (col < 0 || col >= ColorGridCols || row < 0 || row >= ColorGridRows) return;

        int idx = row * ColorGridCols + col;
        if (idx < 0 || idx >= DonChan3D.SharedColorPalette.Length) return;

        switch (activeColorPicker)
        {
            case ColorTarget.Face: DonChan3D.SetFaceColorIndex(idx); break;
            case ColorTarget.Body: DonChan3D.SetBodyColorIndex(idx); break;
            case ColorTarget.Rim: DonChan3D.SetRimColorIndex(idx); break;
        }
        activeColorPicker = ColorTarget.None;
    }

    static void DrawColorPickerGrid()
    {
        if (activeColorPicker == ColorTarget.None) return;

        var palette = DonChan3D.SharedColorPalette;
        int gridW = ColorGridCols * ColorGridCell;
        int gridH = ColorGridRows * ColorGridCell;

        Raylib.DrawRectangle(ColorGridX - 10, ColorGridY - 30, gridW + 20, gridH + 40, new Color((byte)0, (byte)0, (byte)0, (byte)200));

        string label = activeColorPicker switch
        {
            ColorTarget.Face => "Face色を選択（クリック / Esc:閉じる）",
            ColorTarget.Body => "Body色を選択（クリック / Esc:閉じる）",
            ColorTarget.Rim => "Rim色を選択（クリック / Esc:閉じる）",
            _ => ""
        };
        Raylib.DrawText(label, ColorGridX - 5, ColorGridY - 24, 14, Color.White);

        int currentIdx = activeColorPicker switch
        {
            ColorTarget.Face => DonChan3D.FaceColorIndex,
            ColorTarget.Body => DonChan3D.BodyColorIndex,
            ColorTarget.Rim => DonChan3D.RimColorIndex,
            _ => -1
        };

        for (int i = 0; i < palette.Length; i++)
        {
            int col = i % ColorGridCols;
            int row = i / ColorGridCols;
            int x = ColorGridX + col * ColorGridCell;
            int y = ColorGridY + row * ColorGridCell;

            Raylib.DrawRectangle(x + 1, y + 1, ColorGridCell - 2, ColorGridCell - 2, palette[i]);
            bool selected = (i == currentIdx);
            Raylib.DrawRectangleLines(x + 1, y + 1, ColorGridCell - 2, ColorGridCell - 2, selected ? Color.Yellow : Color.Black);
            if (selected)
                Raylib.DrawRectangleLines(x, y, ColorGridCell, ColorGridCell, Color.Yellow);
        }
    }

    // クリック可能な小さな +/- ボタン。呼び出し側で直接押下判定を受け取れるので
    // Draw関数の中からでもそのままIDを増減できる（カラーピッカーグリッドと同じ即時処理方式）。
    const int StepBtnSize = 16;

    static bool DrawStepButton(int x, int y, string label)
    {
        Rectangle r = new Rectangle(x, y, StepBtnSize, StepBtnSize);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Color bg = hover ? new Color((byte)110, (byte)60, (byte)50, (byte)255) : new Color((byte)55, (byte)50, (byte)45, (byte)255);
        Raylib.DrawRectangleRec(r, bg);
        Raylib.DrawRectangleLinesEx(r, 1, hover ? Color.Orange : Color.Gray);
        int tx = (label == "-") ? x + 6 : x + 4;
        Raylib.DrawText(label, tx, y + 1, 14, Color.White);
        return hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    // ID系フィールドをラベル＋数値＋[-][+]ボタンで描画する。onDec/onIncはボタン押下時に呼ばれる。
    static void DrawStepperIntField(int x, int y, string label, int val, Action onDec, Action onInc)
    {
        Raylib.DrawText($"{label}: {val}", x, y + 1, 14, Color.White);

        int btnX = x + 250;
        if (DrawStepButton(btnX, y, "-")) onDec();
        if (DrawStepButton(btnX + StepBtnSize + 4, y, "+")) onInc();
    }


    static void Main()
    {
        Raylib.InitWindow(820, 599, "DonChan3D - Easy Adjust Exporter");
        Raylib.SetTargetFPS(120);

        // 初期アングルとカメラ位置を設定
        DonChan3D.CamPosX = 0.00f;
        DonChan3D.CamPosY = 0.00f;
        DonChan3D.CamPosZ = 20.00f;  // 平行投影用
        DonChan3D.TarPosY = 0.00f;   // 原点中心

        // 回転値の代入
        DonChan3D.ModelRotX = 181.25f;
        DonChan3D.ModelRotY = 27.50f;
        DonChan3D.ModelRotZ = 0.00f;

        DonChan3D.Init();

        while (!Raylib.WindowShouldClose())
        {
            Update();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DarkGray);

            // 3D描画結果を表示（エクスポート中はオフスクリーンへの描画のみで十分なので、
            // 画面プレビュー用の再描画はスキップしてGPU負荷を下げ、書き出し速度を上げる）
            if (!DonChan3D.IsExporting)
            {
                try
                {
                    DonChan3D.DrawSimple();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[DrawSimple ERROR] " + ex);
                }
            }

            // 左側にデバッグ・操作用のUIを重ねて描画
            try
            {
                DrawUI();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DrawUI ERROR] " + ex);
            }

            Raylib.EndDrawing();
        }

        DonChan3D.Unload();
        Raylib.CloseWindow();
    }

    static void Update()
    {
        // 数字入力モード中は他のショートカットを無効化し、テキスト入力だけ処理する
        if (activeField == InputField.CustomHead || activeField == InputField.CustomBody || activeField == InputField.CustomFace)
        {
            HandleNumericInput();
            DonChan3D.Update();
            return;
        }

        // カラーピッカー表示中は他のショートカットを無効化し、グリッドのクリック/Escだけ処理する
        if (activeColorPicker != ColorTarget.None)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) activeColorPicker = ColorTarget.None;
            else HandleColorPickerClick();
            DonChan3D.Update();
            return;
        }

        // 右キー：mirrorを安全にスキップして次へ
        if (Raylib.IsKeyPressed(KeyboardKey.Right))
        {
            int animCount = DonChan3D.GetAnimationCount();
            if (animCount > 0)
            {
                int safety = 0;
                do
                {
                    DonChan3D.NextAnimation();
                    safety++;
                } while (DonChan3D.GetAnimationNameById(DonChan3D.GetCurrentAnimationIndex()).Contains("mirror", StringComparison.OrdinalIgnoreCase) && safety < animCount);
            }
        }

        // 左キー：mirrorを安全にスキップして前へ
        if (Raylib.IsKeyPressed(KeyboardKey.Left))
        {
            int animCount = DonChan3D.GetAnimationCount();
            if (animCount > 0)
            {
                int safety = 0;
                do
                {
                    DonChan3D.PrevAnimation();
                    safety++;
                } while (DonChan3D.GetAnimationNameById(DonChan3D.GetCurrentAnimationIndex()).Contains("mirror", StringComparison.OrdinalIgnoreCase) && safety < animCount);
            }
        }

        // UP/DOWNキーで衣装変更
        if (Raylib.IsKeyPressed(KeyboardKey.Up)) DonChan3D.NextCostume();
        if (Raylib.IsKeyPressed(KeyboardKey.Down)) DonChan3D.PrevCostume();

        // F1：Head番号入力モード開始
        if (Raylib.IsKeyPressed(KeyboardKey.F1))
        {
            activeField = InputField.CustomHead;
            inputBuffer.Clear();
            inputBuffer.Append(DonChan3D.HeadId);
        }

        // F2：Body番号入力モード開始
        if (Raylib.IsKeyPressed(KeyboardKey.F2))
        {
            activeField = InputField.CustomBody;
            inputBuffer.Clear();
            inputBuffer.Append(DonChan3D.BodyId);
        }

        // F3：Face番号入力モード開始
        if (Raylib.IsKeyPressed(KeyboardKey.F3))
        {
            activeField = InputField.CustomFace;
            inputBuffer.Clear();
            inputBuffer.Append(DonChan3D.FaceId);
        }

        // F4/F6/F7：色パレット（63色グリッド）を開く。もう一度押すか[Esc]で閉じる
        if (Raylib.IsKeyPressed(KeyboardKey.F4))
            activeColorPicker = (activeColorPicker == ColorTarget.Face) ? ColorTarget.None : ColorTarget.Face;
        if (Raylib.IsKeyPressed(KeyboardKey.F6))
            activeColorPicker = (activeColorPicker == ColorTarget.Body) ? ColorTarget.None : ColorTarget.Body;
        if (Raylib.IsKeyPressed(KeyboardKey.F7))
            activeColorPicker = (activeColorPicker == ColorTarget.Rim) ? ColorTarget.None : ColorTarget.Rim;

        // F5：表示モード切り替え（デバッグ用：All → Cos → Body → Head → All...）
        // F5：表示モード切り替え（Cosのみ ⇔ Body+Headのみ）
        if (Raylib.IsKeyPressed(KeyboardKey.F5))
        {
            DonChan3D.CurrentDrawMode = (DonChan3D.CurrentDrawMode == DonChan3D.DrawMode.CosOnly)
                ? DonChan3D.DrawMode.BodyHeadOnly
                : DonChan3D.DrawMode.CosOnly;
        }

        // 顔コマ手動送り（本家のコマ対応表を目視確認するための調整用）
        if (Raylib.IsKeyPressed(KeyboardKey.LeftBracket)) DonChan3D.PrevFaceFrameManual();
        if (Raylib.IsKeyPressed(KeyboardKey.RightBracket)) DonChan3D.NextFaceFrameManual();
        if (Raylib.IsKeyPressed(KeyboardKey.Backslash)) DonChan3D.ClearFaceFrameManual();

        // CosOnly側の顔サイズ手動倍率（衣装を変えても保持される）：,/.で微調整、F9でリセット
        if (Raylib.IsKeyPressed(KeyboardKey.Comma)) DonChan3D.SetCosFaceScale(DonChan3D.CosFaceScale - 0.05f);
        if (Raylib.IsKeyPressed(KeyboardKey.Period)) DonChan3D.SetCosFaceScale(DonChan3D.CosFaceScale + 0.05f);
        if (Raylib.IsKeyPressed(KeyboardKey.F9)) { DonChan3D.ResetCosFaceScale(); DonChan3D.ClearHeadFaceScaleManual(); }

        // エクスポート実行キー
        if (Raylib.IsKeyPressed(KeyboardKey.S)) DonChan3D.StartExport();
        if (Raylib.IsKeyPressed(KeyboardKey.A)) DonChan3D.StartBatchExport();

        DonChan3D.Update();
    }

    // 数字入力処理（0-9キー、Backspace、Enterで確定、Escでキャンセル）
    static void HandleNumericInput()
    {
        int key = Raylib.GetCharPressed();
        while (key > 0)
        {
            if (key >= '0' && key <= '9' && inputBuffer.Length < 6)
            {
                inputBuffer.Append((char)key);
            }
            key = Raylib.GetCharPressed();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && inputBuffer.Length > 0)
        {
            inputBuffer.Remove(inputBuffer.Length - 1, 1);
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            if (int.TryParse(inputBuffer.ToString(), out int id))
            {
                if (activeField == InputField.CustomHead) DonChan3D.SetHeadId(id);
                else if (activeField == InputField.CustomBody) DonChan3D.SetBodyId(id);
                else if (activeField == InputField.CustomFace) DonChan3D.SetFaceId(id);
            }
            activeField = InputField.None;
            inputBuffer.Clear();
        }
        else if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            activeField = InputField.None;
            inputBuffer.Clear();
        }
    }

    static void DrawUI()
    {
        // 背景の半透明黒パネル
        Raylib.DrawRectangle(5, 5, 360, 589, new Color((byte)0, (byte)0, (byte)0, (byte)150));

        // カメラ・回転情報の描画
        int y = 15;
        DrawField(15, y, "CamPosX", DonChan3D.CamPosX, activeField == InputField.PosX); y += 18;
        DrawField(15, y, "CamPosY", DonChan3D.CamPosY, activeField == InputField.PosY); y += 18;
        DrawField(15, y, "CamPosZ", DonChan3D.CamPosZ, activeField == InputField.PosZ); y += 18;
        DrawField(15, y, "TarPosY", DonChan3D.TarPosY, activeField == InputField.TarY); y += 18;
        DrawField(15, y, "ModelRotX", DonChan3D.ModelRotX, activeField == InputField.RotX); y += 18;
        DrawField(15, y, "ModelRotY", DonChan3D.ModelRotY, activeField == InputField.RotY); y += 18;
        DrawField(15, y, "ModelRotZ", DonChan3D.ModelRotZ, activeField == InputField.RotZ); y += 18;

        // Head/Body/FaceのID表示（数字入力対応 + クリックの+-ボタン）
        bool stepBlocked = (activeField == InputField.CustomHead || activeField == InputField.CustomBody ||
                             activeField == InputField.CustomFace || activeColorPicker != ColorTarget.None);

        DrawStepperIntField(15, y, "CostumeId [Up/Down]", DonChan3D.CostumeId,
            () => { if (!stepBlocked) DonChan3D.PrevCostume(); },
            () => { if (!stepBlocked) DonChan3D.NextCostume(); });
        y += 18;

        DrawIntFieldStepperRow(15, y, "HeadId [F1]", DonChan3D.HeadId, activeField == InputField.CustomHead, stepBlocked,
            () => DonChan3D.SetHeadId(Math.Max(0, DonChan3D.HeadId - 1)),
            () => DonChan3D.SetHeadId(DonChan3D.HeadId + 1));
        y += 18;

        DrawIntFieldStepperRow(15, y, "BodyId [F2]", DonChan3D.BodyId, activeField == InputField.CustomBody, stepBlocked,
            () => DonChan3D.SetBodyId(Math.Max(0, DonChan3D.BodyId - 1)),
            () => DonChan3D.SetBodyId(DonChan3D.BodyId + 1));
        y += 18;

        DrawIntFieldStepperRow(15, y, "FaceId [F3]", DonChan3D.FaceId, activeField == InputField.CustomFace, stepBlocked,
            () => DonChan3D.SetFaceId(Math.Max(0, DonChan3D.FaceId - 1)),
            () => DonChan3D.SetFaceId(DonChan3D.FaceId + 1));
        y += 18;
        DrawColorField(15, y, "FaceColor [F4]", DonChan3D.FaceColor, false); y += 18;
        DrawColorField(15, y, "BodyColor [F6]", DonChan3D.BodyColor, false); y += 18;
        DrawColorField(15, y, "RimColor  [F7]", DonChan3D.RimColor, false); y += 25;

        // エクスポートステータス
        if (DonChan3D.IsExporting)
        {
            Raylib.DrawText($"EXPORTING: {DonChan3D.ExportProgress}", 15, y, 16, Color.Red);
        }
        else
        {
            Raylib.DrawText("READY FOR EXPORT", 15, y, 14, Color.Green);
        }
        y += 20;

        Raylib.DrawText($"[S]: Single Export   | [A]: All Batch Export", 15, y, 12, Color.SkyBlue); y += 15;
        Raylib.DrawText($"[<- / ->]: Change Anim  | [F1]:Head [F2]:Body [F3]:Face [F4]:Face色選択 [F6]:Body色選択 [F7]:Rim色選択 [F5]:View={DonChan3D.CurrentDrawMode}", 15, y, 12, Color.SkyBlue); y += 15;
        {
            string mode = DonChan3D.IsFaceFrameManual ? $"AUTO+OFS{DonChan3D.FaceFrameManualOffset:+0.##;-0.##}" : "AUTO";
            Raylib.DrawText($"[ / ]:FaceFrame({mode}) {DonChan3D.CurrentFaceFrame}/{DonChan3D.FaceFrameCount - 1}  \\:Clear", 15, y, 12, Color.SkyBlue);
        }
        y += 15;
        {
            string scaleMode = (DonChan3D.CosFaceScale != 1.0f) ? "FIXED" : "AUTO(1.0)";
            Raylib.DrawText($"[,/.]:FaceScale({scaleMode}) {DonChan3D.CosFaceScale:F2}  F9:Reset", 15, y, 12, Color.SkyBlue);
        }
        y += 15;

        // mirrorを除外した有効なアニメーションだけをリストアップする
        int rawAnimCount = DonChan3D.GetAnimationCount();
        int currentRawIdx = DonChan3D.GetCurrentAnimationIndex();

        var validAnims = new System.Collections.Generic.List<(int originalId, string name)>();
        int displayActiveIdx = 0;

        for (int i = 0; i < rawAnimCount; i++)
        {
            string name = DonChan3D.GetAnimationNameById(i);
            if (name.Contains("mirror", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i == currentRawIdx)
            {
                displayActiveIdx = validAnims.Count;
            }
            validAnims.Add((i, name));
        }

        Raylib.DrawText($"--- ANIMATION LIST ({validAnims.Count} anims) ---", 15, y, 14, Color.Yellow); y += 18;

        // 現在選択されている位置の前後だけをスクロール表示（最大14件）
        int start = Math.Max(0, displayActiveIdx - 5);
        int end = Math.Min(validAnims.Count, start + 14);

        for (int i = start; i < end; i++)
        {
            var anim = validAnims[i];
            bool isCurrent = (i == displayActiveIdx);

            Color itemColor = isCurrent ? Color.Orange : Color.LightGray;
            string prefix = isCurrent ? "=> " : "   ";

            string displayName = anim.name;

            if (!isCurrent && IsAnimSkip(displayName))
            {
                itemColor = new Color((byte)120, (byte)120, (byte)120, (byte)255);
                displayName += " (skip)";
            }

            Raylib.DrawText($"{prefix}[{anim.originalId:D2}] {displayName}", 15, y, 13, itemColor);
            y += 16;
        }

        // 数字入力中のオーバーレイ表示
        if (activeField == InputField.CustomHead || activeField == InputField.CustomBody || activeField == InputField.CustomFace)
        {
            Raylib.DrawRectangle(5, 375, 360, 40, new Color((byte)20, (byte)20, (byte)20, (byte)220));
            string labelName = (activeField == InputField.CustomHead) ? "Head ID: "
                : (activeField == InputField.CustomBody) ? "Body ID: "
                : "Face ID: ";
            Raylib.DrawText(labelName + inputBuffer.ToString() + "_", 12, 385, 16, Color.Orange);
            Raylib.DrawText("[Enter]:Confirm  [Esc]:Cancel", 12, 403, 12, Color.Gray);
        }

        DrawColorPickerGrid();
    }

    static void DrawField(int x, int y, string label, float val, bool active)
    {
        Color color = active ? Color.Orange : Color.White;
        string prefix = active ? "> " : "  ";
        Raylib.DrawText($"{prefix}{label}: {val:F2}", x, y, 14, color);
    }

    // HeadId/BodyId/FaceId用：数字入力中は色をオレンジにし、入力モード中や
    // カラーピッカー表示中は+-ボタンのクリックを無効化する（誤操作防止）
    static void DrawIntFieldStepperRow(int x, int y, string label, int val, bool active, bool blocked, Action onDec, Action onInc)
    {
        Color color = active ? Color.Orange : Color.White;
        string prefix = active ? "> " : "  ";
        Raylib.DrawText($"{prefix}{label}: {val}", x, y, 14, color);

        int btnX = x + 250;
        bool dec = DrawStepButton(btnX, y - 1, "-");
        bool inc = DrawStepButton(btnX + StepBtnSize + 4, y - 1, "+");
        if (blocked) return;
        if (dec) onDec();
        if (inc) onInc();
    }

    static void DrawColorField(int x, int y, string label, Color c, bool active)
    {
        Color color = active ? Color.Orange : Color.White;
        string prefix = active ? "> " : "  ";
        string hex = $"{c.R:X2}{c.G:X2}{c.B:X2}";
        Raylib.DrawText($"{prefix}{label}: #{hex}", x, y, 14, color);
        Raylib.DrawRectangle(x + 240, y + 2, 30, 12, c);
        Raylib.DrawRectangleLines(x + 240, y + 2, 30, 12, Color.Black);
    }

    private static bool IsAnimSkip(string name)
    {
        string lower = name.ToLower();
        if (lower.Contains("don_balloon_failure")) return false;
        if (lower.Contains("don_balloon_loop")) return false;
        if (lower.Contains("don_balloon_success")) return false;
        if (lower.Contains("don_combo_max") || lower.Contains("don_full_combo")) return false;
        if (lower.Contains("don_combo")) return false;
        if (lower.Contains("don_entry_loop")) return false;
        if (lower.Contains("don_full_gage")) return false;
        if (lower == "don_result_failure_loop") return false;
        if (lower == "don_result_failure") return false;
        if (lower.Contains("don_result_full_loop")) return false;
        if (lower.Contains("don_miss6")) return false;
        if (lower.Contains("don_miss_normal")) return false;
        if (lower.Contains("don_miss")) return false;
        if (lower.Contains("don_norm_down")) return false;
        if (lower.Contains("don_norm_up")) return false;
        if (lower.Contains("don_normal")) return false;
        if (lower.Contains("don_sabi_start")) return false;
        if (lower.Contains("don_sabi")) return false;
        if (lower.Contains("don_entry_select_loop")) return false;
        if (lower == "don_result_clear") return false;
        if (lower == "don_result_clear_loop") return false;
        if (lower == "don_result_loop") return false;
        if (lower.Contains("don_select_loop")) return false;
        return true;
    }
}
