#!/usr/bin/env python3
"""
Skrypt do tworzenia czarno-białych (grayscale) wersji ikon ICO.
Zachowuje wszystkie rozmiary z pliku źródłowego.
"""
import argparse
import sys
import os
from PIL import Image, ImageEnhance


def show_help():
    """Wyświetla pomoc."""
    help_text = """
ico2grayscale - Konwersja ikon ICO na skalę szarości

UŻYCIE:
    python create_bw_icons.py --input <plik.ico> --output <wynik.ico> [--brightness N]

WYMAGANE ARGUMENTY:
    --input, -i     Plik wejściowy ICO do konwersji
    --output, -o    Plik wyjściowy ICO (zostanie zapisany)

OPCJONALNE ARGUMENTY:
    --brightness N  Jasność 1-100, gdzie 50 = bez zmian (domyślnie: 50)
                    1-49  = ciemniejszy
                    50    = baza (grayscale bez zmian jasności)
                    51-100 = jaśniejszy
    -h, --help      Wyświetl tę pomoc

PRZYKŁADY:
    python create_bw_icons.py -i Code_0.ico -o Code_gray.ico
    python create_bw_icons.py -i Code_0.ico -o Code_light.ico --brightness 70
    python create_bw_icons.py -i Code_0.ico -o Code_dark.ico --brightness 30
"""
    print(help_text)


def convert_to_grayscale(input_ico_path, output_ico_path, brightness=50):
    """
    Konwertuje ikonę ICO na skalę szarości z opcjonalną regulacją jasności.
    Zachowuje wszystkie rozmiary z pliku źródłowego.
    
    Args:
        input_ico_path: Ścieżka do pliku wejściowego ICO
        output_ico_path: Ścieżka do pliku wyjściowego ICO
        brightness: Jasność 1-100, gdzie 50 = bez zmian
    """
    if not os.path.exists(input_ico_path):
        print(f"Błąd: Nie znaleziono pliku: {input_ico_path}", file=sys.stderr)
        sys.exit(1)
    
    # Przelicz brightness (1-100) na mnożnik (0.02 - 2.0)
    # 50 -> 1.0, 1 -> 0.02, 100 -> 2.0
    brightness_factor = brightness / 50.0
    
    # Otwórz plik ICO
    ico = Image.open(input_ico_path)
    
    # Pobierz wszystkie rozmiary z ICO
    sizes = ico.info.get('sizes', set())
    if not sizes:
        # Fallback - standardowe rozmiary ICO
        sizes = {(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)}
    
    print(f"Plik wejściowy: {input_ico_path}")
    print(f"Rozmiary: {sorted(sizes)}")
    print(f"Jasność: {brightness} (mnożnik: {brightness_factor:.2f})")
    
    output_images = []
    
    for size in sorted(sizes, reverse=True):
        try:
            # Załaduj obraz w konkretnym rozmiarze
            ico.size = size
            img = ico.copy()
            img = img.resize(size, Image.Resampling.LANCZOS)
            
            # Konwertuj do RGBA jeśli potrzeba (zachowaj przezroczystość)
            if img.mode != 'RGBA':
                img = img.convert('RGBA')
            
            # Rozdziel kanały
            r, g, b, a = img.split()
            
            # Konwertuj na skalę szarości
            gray_img = Image.merge('RGB', (r, g, b)).convert('L')
            
            # Zastosuj regulację jasności (jeśli != 50)
            if brightness != 50:
                enhancer = ImageEnhance.Brightness(gray_img)
                gray_img = enhancer.enhance(brightness_factor)
            
            # Konwertuj z powrotem na RGBA z oryginalną przezroczystością
            output_rgba = Image.merge('RGBA', (gray_img, gray_img, gray_img, a))
            output_images.append(output_rgba)
            
            print(f"  ✓ {size[0]}x{size[1]}")
            
        except Exception as e:
            print(f"  ✗ {size}: {e}", file=sys.stderr)
    
    if not output_images:
        print("Błąd: Nie udało się przetworzyć żadnego rozmiaru.", file=sys.stderr)
        sys.exit(1)
    
    # Zapisz plik ICO
    output_images[0].save(
        output_ico_path, 
        format='ICO', 
        sizes=[(img.width, img.height) for img in output_images],
        append_images=output_images[1:] if len(output_images) > 1 else []
    )
    print(f"\nZapisano: {output_ico_path}")


def main():
    # Jeśli brak argumentów - wyświetl pomoc
    if len(sys.argv) == 1:
        show_help()
        sys.exit(0)
    
    parser = argparse.ArgumentParser(
        description='Konwersja ikon ICO na skalę szarości',
        add_help=True
    )
    parser.add_argument(
        '-i', '--input',
        required=True,
        help='Plik wejściowy ICO do konwersji'
    )
    parser.add_argument(
        '-o', '--output',
        required=True,
        help='Plik wyjściowy ICO'
    )
    parser.add_argument(
        '--brightness',
        type=int,
        default=50,
        choices=range(1, 101),
        metavar='1-100',
        help='Jasność 1-100, gdzie 50 = bez zmian (domyślnie: 50)'
    )
    
    args = parser.parse_args()
    
    convert_to_grayscale(args.input, args.output, args.brightness)


if __name__ == "__main__":
    main()
