# train_bottles.py
from ultralytics import YOLO

# Загружаем маленькую модель для классификации
model = YOLO("yolov8n-cls.pt")          # можно заменить на yolov8s-cls.pt или yolov11n-cls.pt

# Запускаем обучение
results = model.train(
    data   = r"C:\Users\umdom\source\repos\RtspTest\dataset-bottles",
    epochs = 50,
    imgsz  = 224,
    batch  = 16,
    # patience = 20,          # раскомментируй, если хочешь раннюю остановку
    # device = 0,             # раскомментируй, если есть видеокарта NVIDIA
)