# EMBER — ลูกไฟดวงสุดท้าย

เกมปริศนากลิ้งลูกบอล 3 ด่าน สร้างด้วย Unity 6 (6000.6.9f1, URP) จากกลไก Condition → Action ในแพ็กเกจของรายวิชา

## เปิดโปรเจกต์และเล่น
1. Unity Hub → **Add project from disk** → เลือกโฟลเดอร์ `EMBER` (ใช้ Unity 6000.6.9f1)
2. เปิดฉาก `Assets/_Ember/Scenes/Title.unity` แล้วกด Play
3. หรือเล่นไฟล์ build: `Build/EMBER/EMBER.exe`

| ปุ่ม | การกระทำ |
| --- | --- |
| W A S D / ลูกศร | กลิ้ง |
| Space | กระโดด |
| R | กลับ checkpoint |
| Esc | หยุดเกม |
| F | โหมดถ่ายภาพ (Fly.cs ของ Session 1) |

ผู้ตรวจเข้าด่านไหนก่อนก็ได้จากเมนู **STAGE SELECT**

## โครงสร้าง
| โฟลเดอร์ | เนื้อหา |
| --- | --- |
| `EMBER/Assets/Downloads/Session3`, `Session2`, `Script`, `Animation` | แพ็กเกจของรายวิชา คงไว้ตามเดิม ยกเว้นการเปลี่ยนชื่อ API ที่ Unity 6 ถอดออกหรือเลิกใช้ (ดูหมายเหตุใต้ตาราง) |
| `EMBER/Assets/_Ember/Scripts` | สคริปต์ runtime ของเกม (namespace `Ember`) |
| `EMBER/Assets/_Ember/Editor` | เครื่องมือสร้าง asset, ฉาก, ตรวจสอบ และ build (เมนู `Ember/…`) |
| `EMBER/Assets/_Ember/Art/Models` | โมเดล FBX 55 ชิ้นที่สร้างใน Blender |
| `ArtSource/Blender` | สคริปต์ Blender (`build_models.py`) และไฟล์ `EmberKit.blend` |
| `ArtSource/Audio` | สคริปต์สังเคราะห์เสียง (`synth.py`) และเพลง Trailer (`trailer_music.py`) |
| `ArtSource/Trailer` | สคริปต์ Blender ของ Trailer (`tr_*.py`, `shots.py`) และข้อมูลฉากที่ส่งออกจาก Unity |
| `Docs` | ภาพผังด่าน, ภาพหน้าจอ, Asset sheet |
| `Tools` | สคริปต์ bootstrap และ pipeline แบบ batch |

ไฟล์ของรายวิชาที่แก้ (เปลี่ยนชื่อ API อย่างเดียว ไม่เปลี่ยนพฤติกรรม):
- `ExtendedFlycam.cs` (Session 2) 2 บรรทัด: `Screen.lockCursor` ถูกถอดออกใน Unity 6 จึงเปลี่ยนเป็น `Cursor.lockState`
- `BallController.cs` และ `CanJump.cs` (Session 3) ไฟล์ละ 1 บรรทัด: `Rigidbody.velocity` เป็น `linearVelocity` ซึ่งเป็นชื่อเดียวกับที่ API Updater ของ Unity 6 จะแก้ให้ การแก้ไว้ก่อนทำให้ Unity ไม่ต้องถามเรื่องอัปเดต API ตอนเปิดโปรเจกต์

โปรเจกต์นี้ตัดแพ็กเกจที่เกมไม่ได้ใช้ออกจาก template ด้วย ได้แก่ IAP, Analytics, Collab, Multiplayer Center และ XR Legacy Input เพราะ DLL ของ IAP และ Analytics รุ่นที่เลิกใช้แล้วทำให้ API Updater ของ Unity ขึ้น error

## ไฟล์ส่งงาน
| ไฟล์ | เนื้อหา |
| --- | --- |
| `Deliver/EMBER_Windows.zip` | เกมที่ build แล้ว: แตกไฟล์แล้วเปิด `EMBER/EMBER.exe` |
| `Deliver/EMBER_Project.zip` | โปรเจกต์ Unity (ไม่รวม Library) สคริปต์ Blender สคริปต์เสียง เครื่องมือ และเอกสาร |
| `Docs/EMBER_GDD.pdf`, `Docs/EMBER_GDD.docx` | เอกสารออกแบบเกม (GDD) |
| `Docs/LevelMaps`, `Docs/Screenshots` | ผังด่านทั้ง 3 ด่าน และภาพจากเกม |
| `Deliver/EMBER_Trailer.mp4` | Trailer แบบ Cinematic 3D ยาว 90 วินาที (1920×1080, 24 fps, เสียง AAC stereo) ทำใน Blender |
| `Docs/Trailer` | contact sheet ของ Trailer, ภาพ poster และภาพเทียบฉาก Blender กับเกม |
| `Deliver/EMBER_Tutorial.mp4` | วิดีโอ Tutorial สอนเล่น อัดจากเกมจริง (1920×1080, 30 fps, เสียง AAC stereo) มีคำบรรยายภาษาไทย |
| `Deliver/EMBER_Tutorial_TH.srt` | ซับไตเติลภาษาไทยของวิดีโอ Tutorial (ใช้กับ YouTube หรือโปรแกรมเล่นวิดีโอได้) |
| `Deliver/EMBER_Tutorial_Narration_TH.md` | บทพากย์พร้อมเวลา สำหรับอัดเสียงพากย์ของทีม |
| `Docs/Tutorial` | contact sheet ของวิดีโอ Tutorial และภาพคำบรรยายทุกอัน |

## Trailer (Blender)
Trailer ทั้งเรื่อง render ใน Blender 5.3 จากข้อมูลฉากจริงของเกม จึงไม่มีภาพจับหน้าจอหรือ HUD
1. **ส่งออกฉากจาก Unity:** เมนู **Ember → 6. Export Scenes for Trailer** หรือ `bash Tools/run_pipeline.sh trailer` (ปิด Unity ก่อน) ได้ไฟล์ `ArtSource/Trailer/export/<ฉาก>/scene.json` และ `meshes.bin` ซึ่งเก็บ transform, mesh, material, แสง, particle, ข้อความ, คลิปแอนิเมชัน และค่าของสคริปต์ Condition → Action ทุกตัว
2. **สร้างฉากใน Blender:** `ArtSource/Trailer/tr_build.py` สร้างทั้ง 5 ฉากใหม่ใน `ArtSource/Trailer/EMBER_Trailer.blend` (material, หมอก, ท้องฟ้า, post-processing แบบเดียวกับ URP) แล้วกำกับ 20 ช็อตตาม `shots.py` ระบบเล่นการเชื่อม Condition → Action ของเกมซ้ำ เช่น ลูกไฟกลิ้งทับแผ่นกดแล้วตะเกียงติดและสะพานยก
3. **เพลงและเสียง:** `ArtSource/Audio/trailer_music.py` แต่งเพลงใหม่ด้วยเครื่องดนตรีชุดเดียวกับ `synth.py` (96 BPM ตรงกับจังหวะตัดภาพ) แล้วมิกซ์กับเสียงจริงของเกม ณ เฟรมที่เกิดเหตุการณ์
4. **ข้อความ:** ภาษาอังกฤษใช้ Futura Bold ของเกม ชื่อไทย "ลูกไฟดวงสุดท้าย" วาดด้วย WPF (`ArtSource/Trailer/render_thai_title.ps1`) เพราะ Blender วางสระและวรรณยุกต์ไทยไม่ถูก

| คำสั่ง | งาน |
| --- | --- |
| `bash Tools/run_trailer.sh all` | ทำทุกขั้นต่อจาก export (render final ใช้เวลาราว 80 นาทีบน GTX 1660 Ti) |
| `bash Tools/run_trailer.sh export` | ส่งออกฉากจาก Unity |
| `bash Tools/run_trailer.sh build` | สร้าง `EMBER_Trailer.blend` ใหม่ |
| `bash Tools/run_trailer.sh lookdev` | ภาพจากกล้อง hero ของทุกห้อง สำหรับเทียบกับ `Docs/Screenshots` |
| `bash Tools/run_trailer.sh stills` | ภาพนิ่งช็อตละ 3 ภาพ ที่ `Build/Trailer/stills` |
| `bash Tools/run_trailer.sh animatic` | ฉบับร่างความละเอียด 25% ทั้งเรื่องพร้อมเพลง ที่ `Build/Trailer/EMBER_Trailer_animatic.mp4` |
| `bash Tools/run_trailer.sh final` | render 1920×1080 ไปที่ `Build/Trailer/final` (ถ้าหยุดกลางทาง สั่งซ้ำแล้วจะทำต่อจากเฟรมที่ค้าง) |
| `bash Tools/run_trailer.sh edit` | ตัดต่อใน Blender VSE แล้ว encode เป็น `Deliver/EMBER_Trailer.mp4` |
| `bash Tools/run_trailer.sh check` | ตรวจไฟล์ MP4 (จำนวนเฟรม, fps, ขนาด, เสียง) ผลอยู่ที่ `Build/Trailer/reports/mp4_probe.json` |
| `bash Tools/run_trailer.sh docs` | ภาพ poster 5 ภาพ (เฟรมกำหนดใน `shots.py`) และ contact sheet ของไฟล์ตัดต่อ ไปที่ `Docs/Trailer` |

- **แก้ช็อต:** แก้ช่วงเวลาและข้อความใน `ArtSource/Trailer/shots.py` ส่วนเส้นทางลูกไฟและมุมกล้องอยู่ใน `ArtSource/Trailer/tr_shots.py` (พิกัดแบบ Unity) แล้วสั่ง `build` และ `final` ใหม่ โดยลบโฟลเดอร์ของช็อตนั้นใน `Build/Trailer/final` ก่อน (ถ้าแก้ข้อความให้ลบ `Build/Trailer/final/titles` ด้วย เพราะ `final` ข้ามเฟรมที่มีอยู่แล้ว) จากนั้นสั่ง `edit`, `check` และ `docs`
- **พื้นที่ดิสก์:** เฟรมใน `Build/Trailer` ใช้พื้นที่หลาย GB ลบได้หลังได้ไฟล์ MP4 แล้ว

## Tutorial (วิดีโอสอนเล่น)
ภาพทั้งหมดอัดจาก `EMBER.exe` ตัวจริง (เมนู HUD และเสียงจริงของเกม) สคริปต์กดปุ่มแทนผู้เล่น
1. **ในเกม** (`EMBER/Assets/_Ember/Scripts/Tools/Tutorial`) โหมดนี้ทำงานเฉพาะเมื่อสั่งด้วย `-tutorialRecord`, `-tutorialDry` หรือ `-tutorialSpike` ตอนเล่นปกติไม่มีผลใด ๆ
   - `TutorialRecorder` ล็อกเวลาเกมไว้ 1/30 วินาทีต่อเฟรม (`Time.captureDeltaTime`) จับภาพทุกเฟรมเป็น JPEG และอัดเสียงของเกมให้ตรงกับภาพด้วย `AudioRenderer`
   - `GameClock` ทำให้ fade และเอฟเฟกต์เมนูเดินตามเฟรมที่อัด ตอนเล่นปกติค่าเท่าเดิมทุกอย่าง
   - `DemoPilot` กดปุ่มเสมือน W A S D Space แล้วใส่แรงบิดสูตรเดียวกับ `BallController` ของรายวิชา ลูกไฟจึงกลิ้งด้วยฟิสิกส์จริง กระโดดเมื่อคำนวณแล้วว่าจะลงบนเป้าหมาย และขี่แท่นหรือแพที่เคลื่อนที่ได้
   - `TutorialOverlay` แสดงแผงปุ่มที่ถูกกด (มุมขวาล่าง) และเคอร์เซอร์เมาส์ที่คลิกปุ่มเมนูจริง
   - `TutorialScript` คือลำดับเหตุการณ์ทั้งเรื่อง ส่วน `TutorialDirector` เป็นคำสั่งพื้นฐาน เช่น เดินตามเส้นทาง ข้อความ จอดำ และตรวจผลด้วย check ชุดเดียวกับ AutoPilot
2. **หลังอัด** (`ArtSource/Tutorial`)
   - ข้อความไทยอยู่ใน `captions.json` และวาดด้วย WPF (`render_captions.ps1`) เพราะ TextMeshPro และ Blender วางสระกับวรรณยุกต์ไทยไม่ถูก
   - `tut_mix.py` ผสมเสียงเกมกับเพลงธีมเบา ๆ และเสียงพากย์ (ถ้ามี)
   - `tut_edit.py` ตัดต่อใน Blender VSE แล้วสร้าง MP4, SRT และบทพากย์
   - `tut_check.py` ตรวจไฟล์ MP4, ข้อความที่ทับ HUD และทำ contact sheet

| คำสั่ง | งาน |
| --- | --- |
| `bash Tools/run_tutorial.sh dry` | เล่นสคริปต์ทั้งเรื่องใน editor แบบไม่แสดงภาพ ตรวจว่าทุกช่วง PASS (ปิด Unity ก่อน) ระบุช่วงได้ เช่น `dry 2A 2D` |
| `bash Tools/run_tutorial.sh build` | build เกมใหม่ (เหมือน `run_pipeline.sh build`) |
| `bash Tools/run_tutorial.sh dryplayer` | เล่นสคริปต์ใน `EMBER.exe` แบบไม่แสดงภาพ ใช้เวลาไม่กี่วินาที |
| `bash Tools/run_tutorial.sh preview` | อัดฉบับร่าง 640×360 ในหน้าต่างเล็ก |
| `bash Tools/run_tutorial.sh capture` | อัดฉบับจริง 1920×1080 เต็มจอ ระหว่างนี้ห้ามแตะเมาส์และคีย์บอร์ด |
| `bash Tools/run_tutorial.sh post` | ข้อความ, เสียง, ตัดต่อ และตรวจ (`captions`, `mix`, `edit`, `check` สั่งแยกได้) ใส่ `preview` ต่อท้ายเพื่อทำกับฉบับร่าง |
| `bash Tools/run_tutorial.sh spike` | ทดสอบการจับภาพและเสียง 150 เฟรม ตรวจ 6 ข้อ |

- **แก้ถ้อยคำ:** แก้ `ArtSource/Tutorial/captions.json` แล้วสั่ง `post` ไม่ต้องอัดใหม่ ถ้าต้องการให้ข้อความค้างนานขึ้น ให้ใส่ค่า `hold` แล้วสั่ง `build`, `capture` และ `post`
- **เสียงพากย์:** อัดตามบทใน `Deliver/EMBER_Tutorial_Narration_TH.md` วางไฟล์ไว้ที่ `ArtSource/Tutorial/voice/<รหัส>.wav` แล้วสั่ง `mix`, `edit` และ `check`
- **พื้นที่ดิสก์:** เฟรม JPEG ใน `Build/Tutorial/final/frames` ใช้หลาย GB ลบได้หลังได้ MP4 แล้ว (ถ้าลบ ต้องอัดใหม่ก่อนตัดต่อครั้งถัดไป)

## การทดสอบ
- `bash Tools/run_pipeline.sh autopilot` ให้ AutoPilot เล่นตามทางแก้ทั้ง 3 ด่านในเอดิเตอร์ ผลอยู่ที่ `Logs/autopilot_editor.txt`
- `EMBER.exe -autopilot -autopilotLog <ไฟล์ผล>` ทดสอบชุดเดียวกันในไฟล์ build จริง
- เพิ่ม `-autopilotShots <โฟลเดอร์>` เพื่อเก็บภาพจากเกมจริงระหว่างทดสอบ ได้แก่ เครดิตตอนจบ และปุ่มเมนูตอนถูกชี้ (`Title_Hover.png`, `StageSelect_Hover.png`, `Pause_Hover.png`, `Ending_Hover.png`)
- ผลล่าสุด: ผ่าน 34/34 ทั้งสองแบบ SceneValidator ไม่มี error

## สร้างใหม่ทั้งหมด
ใน Unity: เมนู **Ember → 1. Generate Assets → 2. Build All Scenes → 3. Validate Scenes → 4. Capture → 5. Build Windows Player**

หรือแบบ batch (ปิด Unity Editor ก่อน): `bash Tools/run_pipeline.sh`

- สร้างโมเดลใหม่: `blender.exe -b --factory-startup -P ArtSource/Blender/build_models.py -- --out EMBER/Assets/_Ember/Art/Models --blend ArtSource/Blender/EmberKit.blend --font "EMBER/Assets/Downloads/Session3/Fonts/Futura Bold.TTF"`
- สร้างเสียงใหม่: `"<Blender>/5.3/python/bin/python.exe" ArtSource/Audio/synth.py EMBER/Assets/_Ember/Audio`

หมายเหตุ: การกด Build All Scenes จะเขียนทับฉากที่แก้ด้วยมือ ให้แก้ฉากด้วยมือหลังขั้นตอนนี้เท่านั้น

## ชื่อในเครดิต
- **รายชื่อทีม** อยู่ใน `Tools/team.txt` บรรทัดละ `ชื่อ นามสกุล|รหัส`
  - เกมแสดงรายชื่อเป็นภาพ เพราะฟอนต์ Futura ไม่มีอักษรไทย และ TextMeshPro วางสระกับวรรณยุกต์ไทยไม่ถูก
  - แก้รายชื่อแล้วรัน `powershell -STA -NoProfile -ExecutionPolicy Bypass -File Tools/render_team_credits.ps1` เพื่อสร้างภาพ `EMBER/Assets/_Ember/Art/UI/T_TeamCredits.png` ใหม่
- **ชื่อวิชาและปี** อยู่ใน `EMBER/Assets/_Ember/Editor/ProjectInfo.cs`
- แก้แล้วกด **Build All Scenes** อีกครั้ง
