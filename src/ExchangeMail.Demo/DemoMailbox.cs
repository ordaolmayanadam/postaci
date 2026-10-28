namespace ExchangeMail.Demo;

public sealed record DemoPerson(string Name, string Address, string JobTitle, string Department, string Phone, string Mobile, string Office);

public sealed record DemoFolder(string Id, string? ParentId, string Name);

public sealed record DemoAttachment(string Id, string Name, string ContentType, long Size);

public sealed class DemoMessage
{
    public required string Id { get; init; }
    public required string FolderId { get; set; }
    public required string ConversationId { get; init; }
    public required string Subject { get; init; }
    public required DemoPerson From { get; init; }
    public required IReadOnlyList<DemoPerson> To { get; init; }
    public IReadOnlyList<DemoPerson> Cc { get; init; } = [];
    public required DateTimeOffset Received { get; init; }
    public bool IsRead { get; set; } = true;
    public string Importance { get; init; } = "Normal";
    public string ItemClass { get; init; } = "IPM.Note";
    /// <summary>Only the text this message added (the conversation view shows it without quotes).</summary>
    public required string BodyHtml { get; init; }
    public IReadOnlyList<DemoAttachment> Attachments { get; init; } = [];

    public string Preview => DemoText.PlainText(BodyHtml);
}

public sealed record DemoEvent(
    string Id, string Subject, DateTimeOffset Start, DateTimeOffset End, string Location, DemoPerson Organizer,
    IReadOnlyList<DemoPerson> Attendees, bool IsAllDay = false, string FreeBusy = "Busy",
    string MyResponse = "Accept", string BodyHtml = "", string? SeriesId = null)
{
    /// <summary>An occurrence of a recurring series; the occurrences of one series share <see cref="SeriesId"/>.</summary>
    public bool IsRecurring => SeriesId is not null;
}

public sealed record DemoContact(string Id, DemoPerson Person, string Company);

/// <summary>
/// A made-up mailbox of a logistics company: people, folders, conversations, a week of calendar and contacts, with
/// times relative to now. Names and addresses are invented; the domains use the reserved .example top-level domain.
/// </summary>
public sealed class DemoMailbox
{
    private readonly bool _turkish;
    private int _nextId;

    public DemoMailbox(string language, DateTimeOffset? now = null)
    {
        _turkish = language == "tr";
        Now = now ?? DateTimeOffset.Now;
        var domain = T("kuzeylojistik.example", "harbor.example");
        Company = T("Kuzey Lojistik", "Harbor Logistics");

        var office = T("İstanbul Merkez, Kat 4", "Head Office, 4th floor");
        DemoPerson P(string tr, string en, string titleTr, string titleEn, string deptTr, string deptEn, string ext) =>
            new(T(tr, en), Address(T(tr, en), domain), T(titleTr, titleEn), T(deptTr, deptEn), Phone(ext), Mobile(ext), office);

        Me = P("Deniz Kaya", "Alex Morgan", "Lojistik Planlama Uzmanı", "Logistics Planner", "Planlama", "Planning", "1204");
        var elif = P("Elif Yıldız", "Emma Clarke", "Operasyon Müdürü", "Operations Manager", "Operasyon", "Operations", "1101");
        var mert = P("Mert Aydın", "Daniel Brooks", "Yazılım Geliştirici", "Software Developer", "Bilgi Teknolojileri", "IT", "1310");
        var zeynep = P("Zeynep Arslan", "Olivia Bennett", "İnsan Kaynakları Uzmanı", "HR Specialist", "İnsan Kaynakları", "Human Resources", "1150");
        var can = P("Can Öztürk", "James Turner", "Satış Müdürü", "Sales Manager", "Satış", "Sales", "1220");
        var ayse = P("Ayşe Demir", "Sophie Walker", "Finans Uzmanı", "Financial Analyst", "Finans", "Finance", "1180");
        var burak = P("Burak Şahin", "Liam Foster", "Depo Sorumlusu", "Warehouse Supervisor", "Depo", "Warehouse", "1405");
        var selin = P("Selin Koç", "Chloe Harris", "Pazarlama Uzmanı", "Marketing Specialist", "Pazarlama", "Marketing", "1240");
        var emre = P("Emre Çelik", "Noah Mitchell", "BT Destek Uzmanı", "IT Support Specialist", "Bilgi Teknolojileri", "IT", "1333");
        People = [Me, elif, mert, zeynep, can, ayse, burak, selin, emre];
        Managers = new Dictionary<DemoPerson, DemoPerson>
        {
            [Me] = elif, [burak] = elif, [mert] = emre, [selin] = can, [ayse] = elif, [zeynep] = elif,
        };

        Folders =
        [
            new("root", null, "Top of Information Store"),
            new("inbox", "root", T("Gelen Kutusu", "Inbox")),
            new("drafts", "root", T("Taslaklar", "Drafts")),
            new("sentitems", "root", T("Gönderilmiş Öğeler", "Sent Items")),
            new("deleteditems", "root", T("Silinmiş Öğeler", "Deleted Items")),
            new("junkemail", "root", T("Gereksiz E-posta", "Junk Email")),
            new("outbox", "root", T("Giden Kutusu", "Outbox")),
            new("projects", "root", T("Projeler", "Projects")),
            new("routes", "projects", T("Rota Optimizasyonu", "Route Planning")),
            new("invoices", "root", T("Faturalar", "Invoices")),
            new("newsletters", "root", T("Bültenler", "Newsletters")),
        ];

        Messages = BuildMessages(elif, mert, zeynep, can, ayse, burak, selin, emre);
        Events = BuildEvents(elif, mert, can, ayse, burak, selin, zeynep);
        Contacts = People.Where(p => p != Me)
            .Select(p => new DemoContact(NewId("contact"), p, Company))
            .Append(new DemoContact(NewId("contact"),
                new DemoPerson(T("Kemal Aksoy", "Ethan Reed"), T("kemal.aksoy@denizyolu.example", "ethan.reed@bluewave.example"),
                    T("Satın Alma Müdürü", "Purchasing Manager"), "", Phone("0142"), Mobile("0142"), ""),
                T("Denizyolu Taşımacılık", "Bluewave Shipping")))
            .Append(new DemoContact(NewId("contact"),
                new DemoPerson(T("Nazlı Erdem", "Grace Collins"), T("nazli.erdem@atlasgumruk.example", "grace.collins@atlascustoms.example"),
                    T("Gümrük Müşaviri", "Customs Broker"), "", Phone("0817"), Mobile("0817"), ""),
                T("Atlas Gümrük", "Atlas Customs")))
            .OrderBy(c => c.Person.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo(language), false))
            .ToList();
    }

    public DateTimeOffset Now { get; }
    public string Company { get; }
    public DemoPerson Me { get; }
    public IReadOnlyList<DemoPerson> People { get; }
    public IReadOnlyDictionary<DemoPerson, DemoPerson> Managers { get; }
    public IReadOnlyList<DemoFolder> Folders { get; }
    public List<DemoMessage> Messages { get; }
    public List<DemoEvent> Events { get; }
    public IReadOnlyList<DemoContact> Contacts { get; }

    public string NewId(string kind) => $"demo-{kind}-{++_nextId}";

    private string T(string tr, string en) => _turkish ? tr : en;

    // English numbers use the UK ranges reserved for fiction (020 7946 0xxx, 07700 900xxx).
    private string Phone(string ext) => T($"+90 212 555 {ext[..2]} {ext[2..]}", $"+44 20 7946 0{ext[1..]}");
    private string Mobile(string ext) => T($"+90 532 555 {ext[..2]} {ext[2..]}", $"+44 7700 900{ext[1..]}");

    private static string Address(string name, string domain)
    {
        var ascii = name.ToLowerInvariant()
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
        return ascii.Replace(' ', '.') + "@" + domain;
    }

    private DateTimeOffset Ago(int days, int hour, int minute) =>
        days == 0 ? Now.AddMinutes(-(hour * 60 + minute)) : new DateTimeOffset(Now.Date.AddDays(-days).AddHours(hour).AddMinutes(minute), Now.Offset);

    private List<DemoMessage> BuildMessages(DemoPerson elif, DemoPerson mert, DemoPerson zeynep, DemoPerson can,
        DemoPerson ayse, DemoPerson burak, DemoPerson selin, DemoPerson emre)
    {
        var list = new List<DemoMessage>();
        var conversation = "";

        DemoMessage Add(string folder, DemoPerson from, IReadOnlyList<DemoPerson> to, DateTimeOffset received, string subjectTr,
            string subjectEn, string bodyTr, string bodyEn, bool read = true, string importance = "Normal",
            IReadOnlyList<DemoAttachment>? attachments = null, string itemClass = "IPM.Note", IReadOnlyList<DemoPerson>? cc = null,
            bool sameConversation = false)
        {
            if (!sameConversation) conversation = NewId("conversation");
            var message = new DemoMessage
            {
                Id = NewId("message"), FolderId = folder, ConversationId = conversation, Subject = T(subjectTr, subjectEn),
                From = from, To = to, Cc = cc ?? [], Received = received, IsRead = read, Importance = importance,
                ItemClass = itemClass, BodyHtml = DemoText.Html(T(bodyTr, bodyEn), from.Name),
                Attachments = attachments ?? [],
            };
            list.Add(message);
            return message;
        }

        DemoAttachment File(string nameTr, string nameEn, string type, long size) => new(NewId("attachment"), T(nameTr, nameEn), type, size);

        // A three-message thread: Elif asks, I answer, Elif follows up (newest in the inbox).
        Add("inbox", elif, [Me, burak], Ago(1, 15, 20),
            "Haftalık operasyon toplantısı gündemi", "Weekly operations meeting agenda",
            "Merhaba,\n\nYarınki toplantıda sevkiyat performansını, depo doluluk oranını ve yeni rota planını konuşacağız. Eklemek istediğiniz bir başlık varsa bugün yazabilir misiniz?",
            "Hi both,\n\nTomorrow we will go through shipment performance, warehouse capacity and the new route plan. If there is anything you would like to add, please let me know today.",
            cc: [can]);
        Add("sentitems", Me, [elif], Ago(1, 16, 5),
            "YNT: Haftalık operasyon toplantısı gündemi", "RE: Weekly operations meeting agenda",
            "Merhaba Elif,\n\nKasım kampanyası için ek araç ihtiyacını da gündeme ekleyebilir miyiz? Kısa bir tablo hazırladım, toplantıda paylaşırım.",
            "Hi Emma,\n\nCould we also add the extra vehicles we need for the November campaign? I have prepared a short table and will share it in the meeting.",
            sameConversation: true);
        Add("inbox", elif, [Me], Ago(0, 0, 25),
            "YNT: Haftalık operasyon toplantısı gündemi", "RE: Weekly operations meeting agenda",
            "Harika fikir, ekledim. Tabloyu toplantıdan önce ekibe de gönderirsen herkes hazırlıklı gelir.\n\nTeşekkürler!",
            "Great idea, added. If you send the table to the team before the meeting, everyone can come prepared.\n\nThanks!",
            read: false, sameConversation: true);

        Add("inbox", mert, [Me], Ago(0, 1, 10),
            "Depo yönetim sistemi bu akşam güncelleniyor", "Warehouse management system update tonight",
            "Merhaba,\n\nDepo yönetim sistemi bu akşam 22:00 ile 23:30 arasında güncellenecek. Bu sürede barkod okuyucular çevrim dışı çalışacak; kayıtlar güncelleme bitince kendiliğinden aktarılacak.",
            "Hi,\n\nThe warehouse management system will be updated tonight between 10:00 and 11:30 pm. Barcode scanners will work offline during that time; records will sync automatically once the update is done.",
            read: false, importance: "High");

        Add("inbox", can, [Me, elif], Ago(0, 2, 40),
            "Müşteri ziyareti: hazırlık toplantısı", "Client visit: preparation meeting",
            "Perşembe günkü müşteri ziyaretinden önce sunumu ve teslimat takvimini birlikte gözden geçirelim.",
            "Before Thursday's client visit, let's review the presentation and the delivery schedule together.",
            read: false, itemClass: "IPM.Schedule.Meeting.Request");

        Add("inbox", ayse, [Me], Ago(0, 3, 15),
            "Ekim ayı masraf raporu", "October expense report",
            "Merhaba Deniz,\n\nEkim ayı masraf raporunu ekte bulabilirsin. Yakıt kalemindeki artışı kampanya dönemine bağladım; uygunsa onaylayıp bana geri gönderebilir misin?",
            "Hi Alex,\n\nPlease find the October expense report attached. I put the increase in fuel costs down to the campaign period; if it looks right, could you approve it and send it back?",
            attachments: [File("Masraf_Raporu_Ekim.xlsx", "Expense_Report_October.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 48_640)]);

        Add("inbox", zeynep, [Me], Ago(0, 5, 0),
            "Yeni çalışanlar için oryantasyon programı", "Onboarding programme for new starters",
            "Merhaba,\n\nKasım ayında aramıza katılacak arkadaşlar için oryantasyon programını ekledim. Planlama ekibinin tanıtımı için çarşamba 14:00 uygun mu?",
            "Hello,\n\nI have attached the onboarding programme for the colleagues joining us in November. Would Wednesday at 2 pm work for the planning team's introduction?",
            attachments: [File("Oryantasyon_Programi.pdf", "Onboarding_Programme.pdf", "application/pdf", 312_400)]);

        Add("inbox", burak, [Me], Ago(1, 16, 40),
            "İzmir sevkiyatında gecikme", "Delay on the Bristol shipment",
            "Merhaba,\n\nİzmir'e giden 3 numaralı sevkiyat yoğun trafik nedeniyle yaklaşık iki saat gecikecek. Müşteriye bilgi verdim.",
            "Hi,\n\nShipment no. 3 to Bristol will be about two hours late because of heavy traffic. I have let the customer know.");
        Add("sentitems", Me, [burak], Ago(1, 16, 55),
            "YNT: İzmir sevkiyatında gecikme", "RE: Delay on the Bristol shipment",
            "Teşekkürler Burak. Yarınki rotayı buna göre güncelliyorum.",
            "Thanks Liam. I'm updating tomorrow's route accordingly.",
            sameConversation: true);

        Add("newsletters", selin, [Me], Ago(1, 11, 5),
            "Kasım bülteni taslağı", "November newsletter draft",
            "Kasım bülteninin taslağı hazır. Planlama ekibinden kısa bir başarı hikâyesi eklemek isteriz; birkaç cümle yazabilir misin?",
            "The November newsletter draft is ready. We would love a short success story from the planning team; could you write a few sentences?");

        Add("inbox", emre, [Me], Ago(2, 9, 30),
            "Parola yenileme hatırlatması", "Password renewal reminder",
            "Merhaba,\n\nŞirket hesabınızın parolasının süresi 7 gün içinde dolacak. Windows'ta Ctrl+Alt+Del → Parolayı değiştir ile yenileyebilirsiniz.",
            "Hello,\n\nYour company account password expires in 7 days. You can renew it in Windows with Ctrl+Alt+Del → Change a password.");

        Add("routes", elif, [Me, mert], Ago(3, 14, 0),
            "Rota optimizasyonu sonuçları", "Route optimisation results",
            "Pilot bölgede yeni rota algoritmasıyla günlük kilometre %12 azaldı. Sonuçları ekteki sunumda özetledim.",
            "In the pilot region the new routing algorithm cut daily mileage by 12%. I have summarised the results in the attached deck.",
            attachments: [File("Rota_Sonuclari.pptx", "Route_Results.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", 1_204_224)]);
        Add("routes", mert, [elif, Me], Ago(3, 15, 10),
            "YNT: Rota optimizasyonu sonuçları", "RE: Route optimisation results",
            "Harika sonuç! Algoritmayı gelecek ay tüm bölgelere açabiliriz.",
            "Great result! We can roll the algorithm out to all regions next month.",
            sameConversation: true);

        Add("inbox", can, [Me], Ago(4, 10, 20),
            "Teklif onaylandı", "Proposal approved",
            "Müjde: Ege bölgesi dağıtım teklifimiz onaylandı. Başlangıç tarihi 1 Aralık. Planlama tarafında neye ihtiyaç olduğunu konuşalım.",
            "Good news: our distribution proposal for the South West has been approved. It starts on 1 December. Let's talk about what planning needs.");

        Add("invoices", ayse, [Me], Ago(5, 13, 45),
            "Eylül faturaları", "September invoices",
            "Eylül ayı taşıma faturaları ekte. Muhasebe kayıtları tamamlandı.",
            "The September transport invoices are attached. The accounting entries are complete.",
            attachments: [File("Faturalar_Eylul.zip", "Invoices_September.zip", "application/zip", 2_457_600)]);

        Add("inbox", ayse, [Me, elif], Ago(6, 16, 0),
            "2027 bütçe planlaması", "2027 budget planning",
            "2027 bütçe çalışması için ekiplerin ihtiyaçlarını ay sonuna kadar toplamamız gerekiyor. Şablonu paylaşılan klasöre koydum.",
            "For the 2027 budget we need to collect each team's requirements by the end of the month. I have put the template in the shared folder.");

        Add("inbox", zeynep, [Me], Ago(8, 12, 30),
            "Yılbaşı etkinliği anketi", "End-of-year party survey",
            "Yılbaşı etkinliği için tarih ve mekân tercihlerinizi soruyoruz. Anket cuma gününe kadar açık.",
            "We would like your preferences for the end-of-year party date and venue. The survey is open until Friday.");

        Add("sentitems", Me, [selin], Ago(2, 17, 15),
            "Bülten için başarı hikâyesi", "Success story for the newsletter",
            "Merhaba Selin,\n\nRota optimizasyonu pilotunun sonuçlarını kısaca özetledim, uygun gördüğün gibi kısaltabilirsin.",
            "Hi Chloe,\n\nI have summarised the route optimisation pilot briefly; feel free to shorten it as you see fit.");

        Add("drafts", Me, [can], Ago(0, 0, 50),
            "Kasım kampanyası araç ihtiyacı", "Vehicles for the November campaign",
            "Merhaba Can,\n\nKampanya dönemi için tahmini ek araç ihtiyacı:",
            "Hi James,\n\nOur estimated extra vehicle needs for the campaign period:");

        Add("junkemail", new DemoPerson(T("Kampanya Merkezi", "Prize Centre"), "noreply@kampanya-firsat.example", "", "", "", "", ""), [Me], Ago(2, 3, 12),
            T("Tebrikler! Ödül kazandınız", "Congratulations! You have won a prize"), T("Tebrikler! Ödül kazandınız", "Congratulations! You have won a prize"),
            "Ödülünüzü almak için hemen tıklayın.", "Click now to claim your prize.");

        return list;
    }

    private List<DemoEvent> BuildEvents(DemoPerson elif, DemoPerson mert, DemoPerson can, DemoPerson ayse, DemoPerson burak,
        DemoPerson selin, DemoPerson zeynep)
    {
        var events = new List<DemoEvent>();
        var monday = Now.Date.AddDays(-(((int)Now.DayOfWeek + 6) % 7));

        DemoEvent At(int day, double startHour, double hours, string subjectTr, string subjectEn, string locationTr, string locationEn,
            DemoPerson organizer, IReadOnlyList<DemoPerson> attendees, string? series = null, string response = "Accept",
            string freeBusy = "Busy", string bodyTr = "", string bodyEn = "")
        {
            var start = new DateTimeOffset(monday.AddDays(day).AddHours(startHour), Now.Offset);
            var e = new DemoEvent(NewId("event"), T(subjectTr, subjectEn), start, start.AddHours(hours), T(locationTr, locationEn),
                organizer, attendees, FreeBusy: freeBusy, MyResponse: organizer == Me ? "Organizer" : response,
                BodyHtml: bodyTr.Length == 0 ? "" : DemoText.Html(T(bodyTr, bodyEn), null), SeriesId: series);
            events.Add(e);
            return e;
        }

        // Short titles and an hour or more each, so the week grid reads at a glance.
        At(0, 9, 1.5, "Operasyon toplantısı", "Operations meeting", "Toplantı Odası 3", "Meeting Room 3", elif,
            [Me, burak, can], series: "operations", bodyTr: "Gündem: sevkiyat performansı, depo doluluk oranı, yeni rota planı.",
            bodyEn: "Agenda: shipment performance, warehouse capacity, new route plan.");
        At(0, 14, 1.5, "Kampanya planlaması", "Campaign planning", "Toplantı Odası 1", "Meeting Room 1", Me, [can, selin]);
        At(1, 10, 1, "Tedarikçi görüşmesi", "Supplier call", "Teams", "Teams", Me, [burak]);
        At(1, 12.5, 1, "Öğle yemeği", "Team lunch", "Kafeterya", "Cafeteria", can, [Me, selin], response: "Tentative", freeBusy: "Tentative");
        At(2, 10, 2, "Depo denetimi", "Warehouse inspection", "Ana Depo", "Main Warehouse", burak, [Me, elif],
            bodyTr: "Raf düzeni, yükleme alanı ve iş güvenliği kontrol listesi üzerinden geçeceğiz.\n\nLütfen güvenlik ayakkabısı ve yelek getirin.",
            bodyEn: "We will walk through the shelving layout, the loading bay and the safety checklist.\n\nPlease bring safety shoes and a hi-vis vest.");
        At(2, 14, 1.5, "Yeni ekip tanışma", "New starters", "Eğitim Salonu", "Training Room", zeynep, [Me],
            bodyTr: "Planlama ekibinin yeni çalışanlara tanıtımı.", bodyEn: "Introducing the planning team to new starters.");
        At(3, 10, 1.5, "Müşteri hazırlığı", "Client prep", "Toplantı Odası 2", "Meeting Room 2", can, [Me, elif], response: "NoResponseReceived");
        At(3, 14, 1.5, "Bütçe toplantısı", "Budget review", "Finans Odası", "Finance Room", ayse, [Me, elif],
            bodyTr: "2027 bütçe taslağındaki ekip ihtiyaçlarını gözden geçireceğiz.\n\nŞablonu toplantıdan önce doldurmayı unutmayın.",
            bodyEn: "We will go through the team requirements in the 2027 budget draft.\n\nPlease fill in the template before the meeting.");
        At(4, 9, 1, "Rota demosu", "Routing demo", "Teams", "Teams", mert, [Me, elif]);
        At(4, 15, 1, "Haftalık özet", "Weekly wrap-up", "Teams", "Teams", Me, [burak], series: "wrap-up");

        var fairStart = new DateTimeOffset(monday.AddDays(3), Now.Offset);
        events.Add(new DemoEvent(NewId("event"), T("Lojistik Fuarı", "Logistics Expo"), fairStart, fairStart.AddDays(1),
            T("İstanbul Fuar Merkezi", "Exhibition Centre"), Me, [], IsAllDay: true, FreeBusy: "Free", MyResponse: "Organizer"));

        // Next week, so moving forward in the calendar is not empty.
        At(7, 9, 1.5, "Operasyon toplantısı", "Operations meeting", "Toplantı Odası 3", "Meeting Room 3", elif, [Me, burak, can], series: "operations");
        At(8, 10, 3, "Ege bölgesi başlangıcı", "South West kick-off", "Toplantı Odası 1", "Meeting Room 1", can, [Me, elif, burak]);
        At(11, 15, 1, "Haftalık özet", "Weekly wrap-up", "Teams", "Teams", Me, [burak], series: "wrap-up");
        return events;
    }
}
