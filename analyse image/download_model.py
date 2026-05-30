import requests
import os
import sys

url = "https://huggingface.co/openai/clip-vit-base-patch32/resolve/main/pytorch_model.bin"
# Path to the blob file we saw earlier
target_path = r"C:\Users\GAMING ASUS\.cache\huggingface\hub\models--openai--clip-vit-base-patch32\blobs\a63082132ba4f97a80bea76823f544493bffa8082296d62d71581a4feff1576f.incomplete"

def download_file(url, path):
    print(f"Checking {path}...")
    file_size = 0
    if os.path.exists(path):
        file_size = os.path.getsize(path)
        print(f"Local file size: {file_size} bytes")
    
    headers = {"Range": f"bytes={file_size}-"}
    try:
        response = requests.get(url, headers=headers, stream=True, timeout=30)
        
        if response.status_code == 416:  # Range not satisfiable
            print("Download already complete or range error.")
            return
            
        print(f"Status Code: {response.status_code}")
        
        mode = "ab" if file_size > 0 else "wb"
        with open(path, mode) as f:
            downloaded = file_size
            for chunk in response.iter_content(chunk_size=1024*1024): # 1MB chunks
                if chunk:
                    f.write(chunk)
                    downloaded += len(chunk)
                    if downloaded % (10*1024*1024) == 0 or True: # Print every MB for debugging
                        sys.stdout.write(f"\rProgress: {downloaded / (1024*1024):.2f} MB")
                        sys.stdout.flush()
        print("\nDownload finished.")
    except Exception as e:
        print(f"\nError: {e}")

if __name__ == "__main__":
    download_file(url, target_path)
