# DocuChat

DocuChat, kurumsal belgelerinizi yükleyip içeriklerine dayalı olarak doğal dilde soru sorabildiğiniz, yapay zekâ destekli bir belge sohbet uygulamasıdır. Sisteme eklediğiniz PDF, Word, Excel ve CSV dosyaları otomatik olarak işlenir; ardından sorduğunuz sorular yalnızca bu belgelerin içeriğinden, kaynağa sadık ve akış hâlinde (streaming) yanıtlanır. Amaç, uzun dokümanların içinde arama yapma yükünü ortadan kaldırıp bilgiye sohbet ederek ulaşmaktır.

Uygulama uçtan uca Türkçe için tasarlanmıştır: metin arama, yeniden sıralama ve dil modeli katmanlarının tamamı Türkçe içerikte yüksek isabet verecek şekilde yapılandırılmıştır.

---

## Ne İşe Yarar?

- **Belgeyle sohbet:** Yüklenen belgelerin içeriğinden, uydurmadan ve kaynağa bağlı kalarak yanıt üretir. Belgede karşılığı olmayan sorularda bunu açıkça belirtir.
- **Çok biçimli belge desteği:** PDF, DOCX, DOC, XLSX ve CSV dosyalarını işler. Taranmış (görüntü tabanlı) PDF'ler dâhil, tablo ve görsel içeren belgeleri de anlar.
- **Görselli yanıtlar:** Belgelerdeki şekil ve tablolar analiz edilir; ilgili görseller yanıtın doğru yerinde otomatik olarak gösterilir.
- **Akıllı arama:** Anlamsal (embedding) arama ile anahtar kelime aramasını birleştirir, sonuçları çapraz kodlayıcı ile yeniden sıralar. Böylece hem eş anlamlı ifadeleri hem birebir terimleri yakalar.
- **Sohbet geçmişi ve oturumlar:** Konuşmalar oturumlar hâlinde saklanır; sabitlenebilir, arşivlenebilir, yeniden adlandırılabilir ve dışa aktarılabilir. Takip soruları önceki bağlamı dikkate alır.
- **Geri bildirim ile öğrenme:** Kullanıcı bir yanıtı beğenip beğenmediğini bildirebilir; bu geri bildirim yalnızca o kullanıcının sonraki sorgularının kalitesini iyileştirmek için kullanılır.
- **Yönetim paneli:** Yöneticiler kullanıcı ekleyip düzenleyebilir, Excel ile toplu kullanıcı yükleyebilir, belgeleri yönetip yeniden işleyebilir.

---

## Nasıl Çalışır?

DocuChat, klasik "sorgu-cevap" yerine bir **RAG (Retrieval-Augmented Generation)** hattı kullanır. Kısaca akış şöyledir:

**1. Belge işleme (yükleme sırasında, arka planda):**
Yüklenen dosya biçimine göre uygun yolla metne dönüştürülür — Word ve eski `.doc` dosyaları LibreOffice ile PDF'e çevrilir, PDF'ler Mistral OCR ile okunur, Excel ve CSV dosyaları ise yapısal olarak (hücre hücre) ayrıştırılır. Elde edilen metin, anlamı bozmayacak biçimde parçalara (chunk) bölünür; her parça BGE-M3 modeliyle 1024 boyutlu bir vektöre gömülür ve PostgreSQL veritabanına kaydedilir. Belgedeki görseller ayrıca yapay zekâ ile betimlenir (caption) ve ilgili parçalarla ilişkilendirilir.

**2. Soru yanıtlama (sohbet sırasında):**
Soru geldiğinde önce anlamsal önbellek kontrol edilir; benzer bir soru daha önce yanıtlandıysa yanıt anında döner. Aksi hâlde soru hem vektör araması hem de anahtar kelime araması (BM25) ile en alakalı belge parçalarını getirir; bu adaylar bir çapraz kodlayıcı (reranker) ile yeniden sıralanır ve en isabetli parçalar dil modeline bağlam olarak verilir. Yanıt, kelime kelime akış hâlinde kullanıcıya iletilir ve arka planda kalite denetiminden geçirilir.

---

## Mimari ve Teknolojiler

Proje üç ana bileşenden oluşur ve tamamı tek bir `docker compose` komutuyla ayağa kalkar.

| Katman | Teknoloji |
|---|---|
| **Backend (`api/`)** | .NET 9, Onion (Clean) Architecture — Domain, Application, Infrastructure, API katmanları |
| **Frontend (`client/`)** | React 19, Vite, React Router, Zustand, Tailwind CSS, react-markdown |
| **Yeniden sıralama (`rerank-service/`)** | Python, FastAPI, `BAAI/bge-reranker-v2-m3` çapraz kodlayıcı |
| **Veritabanı** | PostgreSQL 17 + `pgvector` (vektör araması) |
| **Gömme (embedding)** | Ollama üzerinde `bge-m3` (1024 boyut) |
| **Dil modeli** | Mistral — `mistral-large-latest` (ana), `mistral-small-latest` (yardımcı) |
| **OCR ve görsel betimleme** | Mistral OCR (`mistral-ocr-latest`), Pixtral (`pixtral-12b-2409`) |

**Öne çıkan teknik nitelikler:**

- **Hibrit arama:** pgvector ile yoğun (dense) vektör araması ve PostgreSQL tam metin araması (BM25, Türkçe yapılandırması) Reciprocal Rank Fusion ile birleştirilir.
- **Anlamsal önbellek:** Benzer sorular tekrar hesaplanmadan yanıtlanır; bir belge güncellendiğinde veya silindiğinde yalnızca o belgeye ait önbellek kayıtları temizlenir.
- **Kimlik doğrulama:** ASP.NET Identity üzerine kurulu JWT; statik dosya erişimi için HttpOnly çerez desteği. Kullanıcının personel kodu ilk giriş şifresi olarak kullanılır.
- **Dayanıklı belge işleme:** Belgeler sınırlı eşzamanlılıkla bir kuyrukta işlenir; uygulama yeniden başlarsa yarım kalan işler otomatik olarak kaldığı yerden devam eder.
- **Hız sınırlama (rate limiting):** Giriş, şifre sıfırlama, yükleme ve yeniden işleme gibi maliyetli uç noktalar kötüye kullanıma karşı korunur.

---

## Kurulum

Tüm sistem Docker ile paketlenmiştir; yerel makinenize .NET, Node veya Python kurmanıza gerek yoktur. Yalnızca **Docker Desktop** yeterlidir.

### 1. Depoyu klonlayın

```bash
git clone https://github.com/MuhammetEnesinal/DocuChat.git
cd DocuChat
```

### 2. Ortam değişkenlerini hazırlayın

`.env.example` dosyasını `.env` adıyla kopyalayın ve değerleri kendi bilgilerinizle doldurun:

```bash
cp .env.example .env
```

`.env` içinde doldurulması gereken alanlar:

| Değişken | Açıklama |
|---|---|
| `POSTGRES_PASSWORD` | Veritabanı şifresi (kendi belirleyeceğiniz güçlü bir değer) |
| `JWT_SECRET` | JWT imzalama anahtarı — en az 32 karakter, uzun ve rastgele olmalı |
| `MISTRAL_API_KEY` | Ana dil modeli için Mistral API anahtarı |
| `MISTRAL_HELPER_API_KEY` | Yardımcı model için Mistral API anahtarı |
| `EMAIL_PASSWORD` | Şifre sıfırlama e-postaları için SMTP uygulama şifresi |

> `.env` dosyası gizli bilgiler içerdiği için sürüm kontrolüne dâhil edilmez.

### 3. Sistemi başlatın

```bash
docker compose up -d --build
```

İlk başlatmada gömme modeli (`bge-m3`, ~1.2 GB) ve yeniden sıralama modeli (~2.2 GB) bir kez indirilir; bu adım internet hızınıza bağlı olarak birkaç dakika sürebilir. Modeller kalıcı birimlerde saklandığından sonraki başlatmalar hızlıdır. Veritabanı şeması, uygulama ilk açılışta bekleyen göçleri (migration) otomatik uygulayarak kendini kurar.

### 4. Uygulamaya erişin

| Servis | Adres |
|---|---|
| **Web arayüzü** | http://localhost:8080 |
| **API / Swagger** | http://localhost:5026 |
| **Veritabanı** | localhost:5433 |

Ollama ve yeniden sıralama servisleri yalnızca konteyner ağı içinde çalışır ve dışarıya açılmaz. Seçilen bağlantı noktaları, yerel geliştirme servislerinizle (örneğin 5432'deki Postgres) çakışmayacak şekilde belirlenmiştir.

---

## Kullanım

1. **Giriş yapın.** İlk yönetici hesabıyla oturum açın. Yönetici, panelden yeni kullanıcılar oluşturabilir veya Excel şablonuyla toplu olarak ekleyebilir. Her kullanıcının personel kodu, ilk giriş şifresi olarak atanır.
2. **Belge yükleyin.** Yönetim panelinden PDF, Word, Excel veya CSV dosyalarını yükleyin. Belgeler arka planda işlenir; durumları (Bekliyor, İşleniyor, Hazır) listede takip edilebilir.
3. **Soru sorun.** Sohbet ekranından belgelerinize dair sorularınızı yazın. Yanıt akış hâlinde gelir, ilgili görseller yerinde gösterilir. Soru belirsizse sistem netleştirici seçenekler sunabilir; yanıt sonrasında takip soruları önerilir.
4. **Geri bildirim verin.** Yanıtların altındaki beğen/beğenme düğmeleriyle geri bildirimde bulunabilirsiniz. Bu, sonraki sorularınızın kalitesini iyileştirir.
5. **Oturumlarınızı yönetin.** Sohbetleri sabitleyebilir, arşivleyebilir, yeniden adlandırabilir veya dışa aktarabilirsiniz.

---

## Proje Yapısı

```
DocuChat/
├── api/                     # .NET 9 backend (Onion Architecture)
│   ├── DocuChat.Domain/          # Varlıklar, enum'lar — iş kurallarının çekirdeği
│   ├── DocuChat.Application/      # Use case'ler, arayüzler, DTO'lar, doğrulayıcılar
│   ├── DocuChat.Infrastructure/   # Veritabanı, dış servisler, arka plan işleri
│   └── DocuChat.API/              # Controller'lar, kimlik doğrulama, uygulama girişi
├── client/                  # React + Vite frontend
│   └── src/
│       ├── components/           # Arayüz bileşenleri (chat, admin, auth, shared)
│       ├── hooks/                # Durum ve veri yönetimi hook'ları
│       ├── pages/                # Sayfalar (Chat, Admin, Profile, Login …)
│       └── services/             # API istemcisi
├── rerank-service/          # Python FastAPI yeniden sıralama servisi
├── docker-compose.yml       # Tüm sistemin orkestrasyonu
└── .env.example             # Ortam değişkenleri şablonu
```

---

## Yapılandırma

Çalışma zamanı ayarlarının çoğu `docker-compose.yml` üzerinden ortam değişkenleriyle verilir. İnce ayarlar (chunk boyutu, arama eşikleri, önbellek benzerlik eşiği, sohbet geçmişi bütçesi vb.) backend'deki `appsettings.json` dosyasından yönetilir. Öne çıkan bazı ayarlar:

| Ayar | Açıklama |
|---|---|
| `Chunking:Mode` | Belge parçalama stratejisi: `Semantic` (varsayılan) veya `PageBased` |
| `VectorSearch:Bm25Enabled` | Anahtar kelime aramasının (BM25) hibrit aramaya katılması |
| `Cache:SimilarityThreshold` | Bir sorunun önbellekten yanıtlanması için gereken benzerlik eşiği |
| `Reranker:MinScore` | Bir belge parçasının bağlama alınması için gereken asgari alaka skoru |
| `Chat:FollowUpsEnabled` | Yanıt sonrası takip sorusu önerilerinin üretilmesi |

---

## Lisans

Bu depo için ayrı bir lisans dosyası tanımlanmamıştır. Kullanım koşulları için proje sahibiyle iletişime geçiniz.
