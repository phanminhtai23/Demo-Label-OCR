# 📸 Debug Images - Quy Trình Xử Lý Ảnh

Thư mục này chứa các ảnh debug được lưu tự động tại mỗi bước xử lý trong hàm `DetectLabelRegion()`.

## 🔧 Cách Bật/Tắt Lưu Ảnh Debug

Trong file `Form1.cs`, dòng 370:
```csharp
// ✅ BẬT: Lưu ảnh debug
var (rect, box, qrText) = LabelDetector.DetectLabelRegion(roi, currentThreshold, saveDebugImages: true);

// ❌ TẮT: Không lưu ảnh (hiệu năng tốt hơn)
var (rect, box, qrText) = LabelDetector.DetectLabelRegion(roi, currentThreshold, saveDebugImages: false);
```

## 📋 Các Bước Xử Lý & Ảnh Tương Ứng

Mỗi lần detect thành công sẽ tạo 7 file ảnh với timestamp:

### **Format tên file:** `YYYYMMdd_HHmmss_fff_stepX_description.png`

| Bước | Tên File | Mô Tả |
|------|----------|-------|
| **0** | `step0_original.png` | 🖼️ Ảnh gốc từ camera (ROI) |
| **1** | `step1_grayscale.png` | 🌑 Chuyển sang ảnh xám (grayscale) |
| **2** | `step2_gaussian_blur.png` | 🌫️ Làm mượt bằng GaussianBlur (khử nhiễu) |
| **3** | `step3_binary_threshold.png` | ⚫⚪ Nhị phân hóa (threshold) - nhãn trắng/nền đen |
| **4** | `step4_morphology.png` | 🔧 Morphological operations (Open + Close) |
| **5** | `step5_contours_and_rect.png` | 🟢🔴🟡 Vẽ contours + bounding box:<br>- Xanh lá: Tất cả contours<br>- Đỏ: Contour lớn nhất<br>- Vàng: MinAreaRect |
| **6** | `step6_cropped_label_roi.png` | ✂️ Vùng label đã cắt (để detect QR) |

## 📊 Ví Dụ Timeline

```
20251111_143025_123_step0_original.png
20251111_143025_123_step1_grayscale.png
20251111_143025_123_step2_gaussian_blur.png
20251111_143025_123_step3_binary_threshold.png
20251111_143025_123_step4_morphology.png
20251111_143025_123_step5_contours_and_rect.png
20251111_143025_123_step6_cropped_label_roi.png
```

## ⚠️ Lưu Ý

1. **Hiệu năng:** Chỉ bật `saveDebugImages = true` khi cần debug, vì việc ghi file sẽ làm giảm FPS.

2. **Dung lượng:** Mỗi frame tạo 7 ảnh (~1-3MB), hệ thống sẽ nhanh chóng chiếm dung lượng nếu để chạy lâu.

3. **Xóa ảnh cũ:** Nên xóa thường xuyên để giải phóng dung lượng:
   ```powershell
   Remove-Item "resources\images\*.png"
   ```

4. **Phân tích:** So sánh các bước để tìm điểm yếu:
   - Nếu **step3** (binary) không rõ → Điều chỉnh `thresholdValue`
   - Nếu **step4** (morphology) vẫn nhiễu → Tăng iterations
   - Nếu **step5** không tìm được contour → Kiểm tra ánh sáng

## 🎯 Mục Đích Sử Dụng

- ✅ Debug khi không detect được nhãn
- ✅ Tối ưu tham số `thresholdValue`
- ✅ Kiểm tra chất lượng ảnh từ camera
- ✅ Hiểu rõ quy trình xử lý Computer Vision
- ✅ Training/presentation/documentation

---

**Đường dẫn đầy đủ:**  
`demo_ocr_label/bin/Debug/net8.0-windows/resources/images/`
