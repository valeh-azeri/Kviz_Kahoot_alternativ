# Kvizo — pulsuz Kahoot alternativi

Canlı kviz oyunu: kviz yarat, PIN paylaş, sinifdə yarış keçir. **Bütün imkanlar pulsuzdur.**
Eyni proqram iki rejimdə işləyir: **internetsiz lokal şəbəkədə** və **internetə yüklənmiş sayt kimi**.

## Texnologiyalar
C# / .NET 10 · ASP.NET Core MVC + Razor · SignalR · EF Core 10 (Code First) · MSSQL · N-tier

```
Kviz.Entities     modellər (domain)
Kviz.DataAccess   DbContext, Fluent API konfiqurasiyaları, migration-lar, repository-lər
Kviz.Business     servislər, oyun mexanizmi, validasiya, hesabat (Excel/PDF), QR
Kviz.Web          controller-lər, view-lar, SignalR hub, CSS/JS
Kviz.Tests        56 test (xal qaydaları, canlı oyun axını, tapşırıq, kviz, Excel, hesabat, admin)
```

## Tələblər
- .NET 10 SDK
- SQL Server (default instance `.` və ya `.\SQLEXPRESS`). Bağlantı sətri: `Kviz.Web/appsettings.json` → `ConnectionStrings:Default`
- Baza ilk işə salmada **avtomatik yaranır** (migration ilə). Əl ilə baza dəyişikliyi etməyin: əvvəl model, sonra `dotnet ef migrations add <Ad> -p Kviz.DataAccess -s Kviz.DataAccess`

## 1) Lokal şəbəkədə (internetsiz) işlətmək

1. `Firewall-Ac.bat` faylını **bir dəfə**, sağ klik → *Yönətici kimi işlət* ilə açın (5080 portunu açır).
2. `Baslat.bat` faylını işlədin və **pəncərəni bağlamayın**. Sayt yalnız bu pəncərə açıq olanda işləyir. İlk açılış 1-2 dəqiqə çəkir, hazır olanda brauzer özü açılır. Pəncərədə telefonlar üçün ünvanlar çıxacaq, məsələn `http://192.168.1.5:5080`.
3. Brauzerdə `http://localhost:5080` açılır. Qeydiyyatdan keçin (ilk qeydiyyat admin olur), kviz yaradın, **Oyna** düyməsinə basın.

> Brauzer "localhost refused to connect" (ERR_CONNECTION_REFUSED) yazırsa, deməli sayt işləmir: `Baslat.bat` işə salınmayıb, bağlanıb və ya xəta ilə dayanıb. Pəncərədəki son sətirlərə baxın. Ən çox rast gəlinən səbəblər: SQL Server işləmir və ya 5080 portu başqa proqramda tutulub.
4. Host ekranında PIN, ünvan və **QR kod** görünür. Oyunçular QR kodu oxudur və ya ünvana girib PIN yazır. Hesab lazım deyil.

Telefonların kompüterlə **eyni şəbəkədə** olması lazımdır. İki variant:
- **Eyni Wi-Fi:** hamı sinifdəki / evdəki router-ə qoşulur (internet olmaya bilər).
- **Kompüterin hotspot-u:** Windows → *Ayarlar → Şəbəkə və internet → Mobil hotspot* aç. Telefonlar həmin Wi-Fi-a qoşulur, ünvan adətən `192.168.137.1:5080` olur. Host ekranında bir neçə ünvan görünsə, açılan siyahıdan telefonların şəbəkəsinə uyğununu seçin.
- Telefonda "internet yoxdur" xəbərdarlığı çıxsa, *yenə də qal* seçin (bəzi telefonlar internetsiz Wi-Fi-dan avtomatik ayrılır).

Heç bir xarici skript, şrift və ya CDN istifadə olunmur, hamısı kompüterdə işləyir.

## 2) İnternetə yükləmək

`appsettings.json` (və ya hosting mühit dəyişənləri) üzərində:

| Ayar | Nə üçün |
|---|---|
| `ConnectionStrings:Default` | hosting SQL Server bağlantısı |
| `Kviz:PublicBaseUrl` | saytın ünvanı, məs. `https://kviz.example.com`. QR kod və qoşulma linkləri bundan qurulur |
| `Kviz:RedirectToHttps` | `true`: HTTP-ni HTTPS-ə yönləndirir |
| `Kviz:UseForwardedHeaders` | `true`: reverse proxy / yük balanslaşdırıcı arxasında |
| `Kviz:DataProtectionPath` | cookie açarlarının saxlandığı qovluq (yenidən başlamada girişlər pozulmasın) |
| `Kviz:AutoMigrate` | `true`: baza avtomatik yaranır/yenilənir |
| `Kviz:ImageSearch:Contact` | internetdən şəkil axtarışı üçün sahibin əlaqə ünvanı (e-poçt və ya sayt), Wikimedia tələbi |

```
dotnet publish Kviz.Web -c Release -o publish
```
- IIS-də **WebSocket** funksiyası qurulu olmalıdır (olmasa SignalR özü uzun sorğuya keçir, yenə işləyir).
- `wwwroot/uploads` qovluğuna yazma icazəsi verin (şəkillər orada saxlanılır).
- Eyni sayt həm internetdə, həm lokalda eyni kodla işləyir, yalnız ayarlar fərqlənir.

## Suallarda şəkil (iki ayrı şəkil)
Hər sualın **iki ayrı şəkli** ola bilər və ikisi **alt-alta** göstərilir (əvvəl sualın şəkli, altında əlavə şəkil):
1. **Sualın şəkli:** sizin öz şəkliniz (yapışdırılmış, yüklənmiş, ekrandan kəsilmiş). Sualın özü ola bilər, mətn boş qala bilər. İnternetlə əlaqəsi yoxdur.
2. **Əlavə şəkil:** sualla bağlı illüstrasiya. İnternetdən tapıla (müəllif və lisenziya ilə) və ya kompüterdən yüklənə bilər. Sualın şəklini əvəz etmir.

## Suallarda şəkil (təfərrüatlar)
- Kompüterdən yüklə, **sual mətni sahəsinə Ctrl+V ilə yapışdır** (Word, PDF və ya ekranın kəsilmiş hissəsi: Win+Shift+S), "Yapışdır" düyməsi, faylı sürüklə-burax. Yapışdırılan şəkil sizin öz şəklinizdir, internetdəki heç nə ilə bağlı deyil. Böyük ekran görüntüləri avtomatik kiçildilir. Mətn kopyalayıb yapışdıranda (məsələn Excel-dən) isə mətn olduğu kimi yapışır. **Sual yalnız şəkil də ola bilər** (mətn boş qala bilər), şəkil hem host ekranında, hem oyunçu telefonunda göstərilir.
- **İnternetdən tap, tək-tək axtarış** (əlavə şəkil üçün): qutuya söz yazsanız **yalnız həmin söz** axtarılır (Google kimi), mənbə **Openverse** (Flickr və s.) + **Wikimedia Commons**, hər səhifədə ~35 şəkil və "Daha çox göstər". Hər şəkildə mənbə nişanı və müəllif/lisenziya var. Bir mənbə limitə çatsa digəri işləməyə davam edir. Qutu boşdursa, təkliflər sualın cavabından və açar sözlərindən götürülür.
- **Avtomatik axtarış** ("Suallara əlavə şəkil tap"): bütün suallar üçün yüngül variant (cavab və açar söz, az sorğu, yaddaş). Wikipedia və Wikimedia Commons-dan (pulsuz lisenziyalı) şəkillər təklif olunur. Seçilən şəkil **serverə yüklənir**, ona görə oyun zamanı internet lazım olmur. Müəllif və lisenziya şəklin altında göstərilir. "Suallara əlavə şəkil tap" düyməsi əlavə şəkli olmayan hamısına ən uyğun şəkli avtomatik qoyur (sualların öz şəkillərinə toxunmur, sonra yoxlayıb dəyişə bilərsiniz).
- İnternet yoxdursa bu düymələr "internet yoxdur" deyir, qalan hər şey işləyir. Şəkil mənbəyi yalnız `upload.wikimedia.org`-dur.
- **Vacib ayar:** Wikimedia qaydalarına görə sorğu göndərən proqram sahibinin əlaqə ünvanını göstərməlidir. `Kviz:ImageSearch:Contact` ayarına öz e-poçtunuzu və ya saytınızı yazın. Yazılmasa Wikimedia sorğuları tez-tez "çox sorğu" ilə rədd edir, axtarış yavaş olur və ya "bir az sonra yenidən yoxlayın" çıxır. Şəkil uyğunluğu mövzudan asılıdır: məşhur obyektlərdə yaxşıdır, dar yerli mövzularda təkliflərə baxıb seçmək və ya axtarış sözünü özünüz yazmaq lazım ola bilər.

## İmkanlar
- **8 sual növü:** Test (bir/bir neçə düzgün), Doğru/Yanlış, Cavabı yaz, Sürüşdürücü, Puzzle (sıralama), Sorğu, Söz buludu, Açıq sual; şəkil, vaxt limiti, 2× xal
- **Rejimlər:** Canlı oyun, Komanda oyunu (avtomatik bölgü), Tapşırıq (son tarixə qədər öz tempi ilə)
- Sürətə görə xal, ardıcıl düzgün cavab bonusu, canlı liderlər cədvəli (**hər sualdan, 2, 3, 5, 10 sualdan bir və ya yalnız sonda**, oyun qurarkən seçilir), podium
- **“Liderləri əvvəl mən görüm”** (canlı və komanda oyunu, oyun qurarkən): cədvəl və podium əvvəl yalnız hostun ekranında görünür, oyunçuların telefonunda yer, ümumi xal və cədvəl (serverdən də) gizli qalır. Host “Oyunçulara göstər” deyəndə açılır. Cədvəl açılmadan keçid edilsə, host təsdiq soruşulur
- **Kim hansı variantı seçdi:** sual nəticəsində hostun ekranında sütunun üstünə gələndə (telefonda/planşetdə toxunanda) həmin variantı seçən oyunçular, komanda oyununda isə komandalar və onların oyunçuları görünür
- **Avatarlar** (canlı və komanda oyunu): oyunçu qoşularkən və gözləmə otağında telefonundan 40 hazır avatardan (heyvanlar, personajlar, əyləncə; internetsiz işləyir) birini seçir və ya **telefondan öz şəklini** yükləyir. Şəkil telefonda avtomatik **160×160**-a kəsilib kiçildilir (adətən 3-20 KB), server isə real JPEG olduğunu, ölçüsünü və 80 KB-dan böyük olmadığını yenidən yoxlayır. Seçilməyibsə təsadüfi avatar verilir. Avatar hostun siyahısında, liderlər cədvəlində, podiumda və oyunçunun üst panelində görünür. Oyun qurarkən “Oyunçular öz şəklini yükləyə bilsin” söndürülə bilər (siniflər üçün). Yüklənən şəkillər `wwwroot/uploads/avatars/` qovluğunda saxlanır və oyun/hesabat silinəndə (və ya oyunçu avatarını dəyişəndə) avtomatik silinir. Tapşırıq rejimində avatar yoxdur
- **Güclər:** 2× xal, 50/50, seriya qoruyucusu
- Kviz kitabxanası: hamıya aç, axtar, kateqoriya/dil, sevimlilər, kopyala
- **Hesabat:** sual və oyunçu üzrə statistika, **Excel və PDF** ixracı; Excel-dən kviz idxalı/ixracı
- Səs effektləri (internetsiz, brauzerdə yaradılır), telefon daxil bütün ekranlarda uyğunlaşan dizayn

## Admin paneli (`/Admin`)
- **Kim admin olur:** `appsettings.json` → `Kviz:AdminEmails` siyahısındakı e-poçtlar. Siyahı boşdursa, ilk qeydiyyatdan keçən admin olur. Siyahı doldurulubsa, yalnız həmin e-poçtlar admin olur. Mövcud admin başqasını da admin edə bilər.
- **İnternetdə diqqət:** e-poçt təsdiqi olmadığı üçün admin e-poçtu ilə **ilk qeydiyyatdan keçən** admin olur. Saytı yüklədikdən dərhal sonra, ictimaiyyətə açmazdan əvvəl öz hesabınızı həmin e-poçtla yaradın və güclü parol seçin. Bundan sonra o e-poçtla başqası qeydiyyat edə bilməz.
- **Ümumi:** istifadəçi, kviz, oyun statistikası, son qeydiyyatlar və oyunlar
- **İstifadəçilər:** axtarış, hesab yaratmaq, bloklamaq (açıq sessiya da bağlanır), admin etmək/rolu götürmək, **parolu sıfırlamaq** (müvəqqəti parol bir dəfə göstərilir), silmək (kvizləri və oyunları ilə)
- **Kvizlər:** hamısını görmək, kitabxanadan çıxarıb **kilidləmək** (sahibi yenidən açıq edə bilməz), silmək
- **Oyunlar:** davam edən oyunu bağlamaq, hesabatı silmək
- **Kateqoriyalar:** kateqoriya əlavə etmək, adını/emojisini dəyişmək, sırasını ▲▼ ilə təyin etmək, silmək (silinəndə həmin kateqoriyanın kvizləri silinmir, sadəcə kateqoriyasız qalır). Dəyişiklik kitabxananın süzgəcində və kviz redaktorunda dərhal görünür.
- **Ayarlar:** **qeydiyyatı açıb-bağlamaq** (bağlı olanda hesabları yalnız admin yaradır) və **kvizlərə qeydiyyatsız baxışı açıb-bağlamaq** (bağlı olanda kitabxana və kviz səhifələri yalnız daxil olanlara görünür). Hər iki halda oyunçular yenə də PIN ilə qoşula bilir.
- Özünüzü bloklamaq/silmək və sonuncu adminin rolunu götürmək mümkün deyil.

Oflayn (lokal şəbəkə) rejimində qeydiyyat da internet tələb etmir. Hesablar bu kompüterdəki bazada saxlanılır və e-poçt təsdiqi yoxdur. Eyni şəbəkədəki hər kəs `http://<IP>:5080/Account/Register` ünvanından qeydiyyat edə bilər. Bunu istəmirsinizsə admin panelində qeydiyyatı bağlayın.

## Məlum məhdudiyyətlər
- Canlı oyunlar yaddaşda işləyir: server yenidən başlasa yarımçıq canlı oyun ləğv olunur (tapşırıqlar bazada olduğu üçün davam edir). Bir server nüsxəsi nəzərdə tutulub.
- E-poçt göndərilmədiyi üçün "parolu unutdum" yoxdur (parolu profildə dəyişmək olur).
- Qeydiyyat açıqdır, e-poçt təsdiqi yoxdur.

## Testlər
```
dotnet test
```
