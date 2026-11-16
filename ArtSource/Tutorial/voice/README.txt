เสียงพากย์ของวิดีโอ Tutorial EMBER

วางไฟล์เสียงพากย์ไว้ในโฟลเดอร์นี้ ไฟล์ละหนึ่งบรรทัด ชื่อไฟล์ตามรหัสคำบรรยาย เช่น s1_roll.wav
ดูรายการรหัส เวลา และจำนวนวินาทีที่พูดได้ใน Deliver/EMBER_Tutorial_Narration_TH.md

- รับ WAV ทุกแบบ: mono หรือ stereo, 44.1 หรือ 48 kHz, 16/24-bit หรือ float
- ไม่ต้องตัดความเงียบหัวท้าย และไม่ต้องปรับความดัง สคริปต์ทำให้
- เสียงจะเริ่ม 0.15 วินาทีหลังคำบรรยายขึ้นจอ เสียงเกมกับเพลงจะเบาลงระหว่างพูด

เมื่อวางไฟล์ครบแล้ว รัน (ใน Git Bash ที่โฟลเดอร์โปรเจกต์):
    bash Tools/run_tutorial.sh mix
    bash Tools/run_tutorial.sh edit
    bash Tools/run_tutorial.sh check
ไฟล์ที่ยาวเกินช่องจะมีคำเตือนใน Build/Tutorial/reports/mix_final.json
