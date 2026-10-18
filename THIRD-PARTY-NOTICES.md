# Thông báo bên thứ ba

Làm 1 Chạm đóng gói các thành phần sau.

## Fluent UI System Icons

- Nguồn: https://github.com/microsoft/fluentui-system-icons (commit a347af95478f989038cccbf085978865264c048a)
- Dùng: biến thể `*_color.svg`, chuyển sang PNG 96 px tại `src/LamMotCham.App/Assets/Icons/png/`.
- Giấy phép: MIT

```
MIT License

Copyright (c) 2020 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## .NET Runtime và WPF

Bản EXE self-contained đóng gói .NET Runtime và Windows Desktop Runtime của Microsoft (giấy phép MIT). Xem https://github.com/dotnet/runtime/blob/main/LICENSE.TXT và https://github.com/dotnet/wpf/blob/main/LICENSE.TXT.

## Phần mềm được cài qua WinGet

Làm 1 Chạm không đóng gói và không phân phối lại các ứng dụng trong danh mục (Chrome, Brave, UniKey, 7-Zip, Adobe Acrobat Reader, SumatraPDF, VLC, Microsoft 365, Zalo, Zoom). WinGet tải chúng từ nguồn của nhà phát hành; mỗi ứng dụng có điều khoản riêng mà người dùng chấp nhận khi xác nhận cài đặt. Riêng logo của Brave và icon của 7-Zip được đóng gói trong ứng dụng để hiển thị nhận diện, lấy từ kho mã nguồn chính thức: `brave/brave-core` (app/theme/brave/product_logo_256.png) và `ip7z/7zip` (CPP/7zip/UI/FileManager/FM.ico). Logo là nhãn hiệu của chủ sở hữu tương ứng, chỉ dùng để nhận diện sản phẩm. Các gói còn lại dùng icon theo danh mục cho đến khi có logo được xác minh nguồn.
