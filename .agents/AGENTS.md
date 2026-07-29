# VCL - Quy ước làm việc chung cho AI coding

File này áp dụng cho toàn bộ workspace `vcl`. Nếu một thư mục con có
`AGENTS.md` riêng, phải tuân thủ cả hai; quy tắc gần file đang sửa hơn được
ưu tiên khi có khác biệt về stack.

## 1. Bản đồ workspace

| Thư mục | Vai trò | Stack và lệnh chuẩn |
| --- | --- | --- |
| `vcl-BE/VietnamCrossBorderLogictisSystem` | Backend API | ASP.NET Core, .NET 8, EF Core; `dotnet build VietnamCrossBorderLogictisSystem.slnx` |
| `vcl-forntend` | Web nội bộ cho Admin/Sale/Operations | Next.js 16, React 19, JavaScript; dùng Bun; `bun run lint`, `bun run build` |
| `vcl-customer-web/logictic-customer` | Web khách hàng | React 19, Vite, JavaScript; dùng npm; `npm run lint`, `npm run build` |

Không đổi package manager, framework, cấu trúc thư mục hoặc thêm dependency
nếu yêu cầu hiện tại không thực sự cần.

## 2. Nguyên tắc bắt buộc

1. Trước khi sửa, đọc `README.md`, rule gần nhất, `git status` và các file
   liên quan. Không ghi đè hoặc hoàn tác thay đổi chưa commit của người khác.
2. Chỉ sửa phạm vi cần thiết cho task. Không format toàn repo, đổi tên hàng
   loạt hoặc refactor ngoài phạm vi.
3. Bám convention của module đang sửa: tên rõ nghĩa, hàm ngắn, early return,
   tránh lặp logic, tránh magic value và comment giải thích "what". Comment chỉ
   dùng để giải thích quyết định hoặc ràng buộc không hiển nhiên.
4. Không đoán API, field, enum, role hoặc trạng thái nghiệp vụ. Nguồn sự thật
   là controller/DTO/Swagger của backend và tài liệu contract đã được team
   thống nhất. Nếu thiếu contract, nêu rõ điểm thiếu trước khi triển khai.
5. Không hard-code URL môi trường, token, mật khẩu, connection string hoặc dữ
   liệu nhạy cảm. Dùng biến môi trường và file example; không commit `.env`,
   secret, log chứa credential/token/PII.
6. Không tạo hoặc commit output sinh tự động như `node_modules`, `.next`,
   `dist`, `bin`, `obj`. Chỉ tạo EF migration khi task có thay đổi schema.
7. Không commit, push, merge, rebase hoặc sửa lịch sử Git nếu người dùng chưa
   yêu cầu rõ ràng.

## 3. Quy tắc theo dự án

### Backend (`vcl-BE`)

- Giữ đúng phân tầng hiện có:
  - `VCL_API`: controller, middleware, DI và HTTP concerns.
  - `VCL_BLL`: nghiệp vụ, DTO, mapping, service/interface.
  - `VCL_DLL`: entity, `DbContext`, repository, migration và persistence.
- Controller phải mỏng; không đặt business logic hoặc truy vấn EF trực tiếp
  trong controller.
- Dùng dependency injection, async API của EF Core và truyền
  `CancellationToken` cho luồng I/O mới khi khả thi.
- Validate input tại biên hệ thống; trả HTTP status và error shape nhất quán.
  Không trả stack trace, entity EF hoặc thông tin nội bộ ra client.
- Phân quyền phải được kiểm tra phía server. Không coi route guard của frontend
  là biện pháp bảo mật.
- Tiền tệ dùng kiểu chính xác (`decimal`), thời gian lưu/trao đổi theo UTC, enum
  hoặc trạng thái phải có một định nghĩa thống nhất.
- Khi đổi entity/schema: tạo migration có tên mô tả, kiểm tra migration và cập
  nhật DTO/mapping/contract liên quan; không sửa migration cũ đã chia sẻ.

### Web nội bộ (`vcl-forntend`)

- Đây là Next.js App Router bằng JavaScript. Trước khi dùng API Next.js mới,
  đọc tài liệu phù hợp trong `node_modules/next/dist/docs/` theo rule cục bộ.
- UI không gọi `apiRequest` trực tiếp. Domain logic đặt tại
  `src/modules/<feature>/` với `api.js`, `mock.js`, `mappers.js`, `index.js`;
  UI chỉ import public facade từ `@/modules/<feature>`.
- Feature hỗ trợ mock phải giữ hành vi và response shape đồng nhất giữa mock và
  API. Mapper chịu trách nhiệm normalize dữ liệu tại boundary.
- Route dùng constant trong `src/utils/appRoutes.js`; khi thêm vùng truy cập,
  cập nhật đồng bộ `src/utils/routeAccess.js`, middleware và guard liên quan.
- Mặc định là Server Component; chỉ thêm `"use client"` khi component thực sự
  cần state, effect hoặc browser API.
- Dùng alias `@/` cho import từ `src`; giữ component dùng riêng bên cạnh route,
  component dùng chung ở `src/app/components`.

### Web khách hàng (`vcl-customer-web/logictic-customer`)

- Giữ API call trong `src/api`, dùng Axios instance chung ở `src/api/axios.js`;
  page/component không tự tạo base URL hoặc Axios instance mới.
- Route được khai báo tập trung trong `src/routes/AppRouter.jsx`. Component
  React dùng PascalCase, hook dùng tiền tố `use`, utility/service dùng
  camelCase.
- State là immutable; dùng functional state update khi giá trị mới phụ thuộc
  giá trị cũ. Effect phải cleanup subscription/timer/request khi cần.
- Giữ style cạnh component theo convention hiện tại. Không trộn thêm UI
  framework cho một màn hình nếu component/thư viện hiện có đáp ứng được.
- Biến public phía Vite phải dùng `VITE_`; tuyệt đối không đặt secret vào biến
  client.

## 4. Quy trình UI-first khi backend chưa hoàn thiện

Frontend không chờ backend mới dựng giao diện. Khi backend/endpoint chưa sẵn
sàng, AI phải chốt **proposed contract** trước, dựng đầy đủ màn hình bằng mock,
sau đó kết nối API qua adapter mà không đổi props hoặc state của UI.

### Một canonical data model cho mỗi domain

- Mỗi khái niệm nghiệp vụ chỉ có một model chuẩn dùng lại cho list, detail,
  form, dashboard và giữa hai frontend. Ví dụ `Consignment` phải giữ cùng tên
  và ý nghĩa field ở màn staff lẫn customer; không tạo các biến thể như
  `staffConsignment`, `customerOrder` nếu chúng cùng là một entity.
- Dữ liệu từ API phải được normalize tại mapper/service trước khi vào UI.
  Component chỉ nhận canonical model và không đọc đồng thời nhiều alias kiểu
  `id ?? orderId ?? consignmentId`.
- Field chung giữa list và detail phải cùng tên, kiểu và ý nghĩa. Detail có thể
  thêm field nhưng không được đổi shape của field đã có ở list.
- Dữ liệu domain giữ dạng thô: tiền là number chưa format, ngày giờ là chuỗi
  ISO 8601 UTC, trạng thái là machine value ổn định. Format tiền/ngày và label
  trạng thái tại presentation layer.
- ID luôn là `string`; collection rỗng dùng `[]`; giá trị tùy chọn dùng `null`;
  boolean dùng `true/false`. Không dùng lẫn `undefined`, chuỗi rỗng, `0` để biểu
  diễn cùng một trạng thái thiếu dữ liệu.
- Enum/status, option và label dùng constant/map tập trung theo domain; không
  khai báo lại ở từng màn hình.
- Create/update payload là model input riêng, chỉ chứa field backend cho phép;
  không gửi nguyên detail/view model hoặc field chỉ phục vụ hiển thị.

Shape frontend tối thiểu:

```js
// Entity dùng chung; domain cụ thể được phép bổ sung field.
const entity = {
  id: "string",
  code: "string",
  status: "PENDING",
  createdAt: "2026-07-29T08:00:00.000Z",
  updatedAt: null,
};

const listResult = {
  items: [entity],
  pagination: {
    page: 1,
    pageSize: 20,
    totalItems: 1,
    totalPages: 1,
  },
};

const appError = {
  code: "VALIDATION_ERROR",
  message: "Thông báo có thể hiển thị cho người dùng",
  fieldErrors: {
    fieldName: ["Lỗi của field"],
  },
};
```

Wire response của backend có thể khác trong giai đoạn chuyển tiếp, nhưng mapper
phải đưa về đúng các shape trên. Với API mới, ưu tiên để backend trả trực tiếp
shape này nhằm giảm adapter không cần thiết.

### Contract và mock phải đi trước component

Với mỗi feature UI mới, thứ tự bắt buộc là:

1. Lập danh sách màn hình và user flow; xác định list/detail/create/update cùng
   các quyền và hành động nghiệp vụ.
2. Tạo tài liệu contract trong thư mục `doc/` hoặc `docs/` hiện có, gồm endpoint
   dự kiến, request, success response, error response, enum/status và ít nhất
   một JSON example. Gắn nhãn `proposed` cho phần backend chưa xác nhận.
3. Khai báo canonical model bằng JSDoc/schema/constant trong domain module,
   đồng thời viết mapper và self-check cho dữ liệu biên.
4. Tạo mock/factory dùng đúng canonical model. Mock phải deterministic, đủ dữ
   liệu cho pagination, filter, permission và mọi trạng thái quan trọng.
5. UI chỉ gọi facade/service. Cùng một function signature và result shape được
   dùng cho mock lẫn API để khi backend hoàn thành chỉ đổi data source.
6. Sau khi backend xác nhận, cập nhật tài liệu từ `proposed` thành `confirmed`
   và kiểm tra lại mapper/mock/consumer bằng contract self-check.

Không hard-code object mẫu trực tiếp trong page/component, không để mỗi màn tự
fetch/map cùng một entity, và không sửa UI chỉ để thích nghi với response lệch;
hãy sửa adapter tại boundary.

### Tiêu chuẩn một màn hình UI hoàn chỉnh

Mỗi màn có dữ liệu phải triển khai tối thiểu:

- layout responsive và tái sử dụng shell/design token/component chung;
- loading/skeleton, empty, error có retry, success feedback;
- dữ liệu dài, thiếu field tùy chọn và danh sách nhiều trang;
- validation phía client khớp proposed contract, chống submit lặp;
- trạng thái disabled/hidden theo permission nhưng không thay thế kiểm tra
  quyền phía backend;
- keyboard/focus/label cơ bản và không dùng chỉ màu sắc để truyền đạt trạng
  thái.

Nếu task chỉ yêu cầu prototype tĩnh, AI vẫn phải dùng fixture theo canonical
model và facade mock; không nhúng dữ liệu giả rải rác trong JSX.

## 5. Đồng bộ contract giữa các team

Thay đổi API được xem là thay đổi liên dự án khi có một trong các nội dung:
endpoint/method/status code, request/response field, pagination, enum/status,
role/permission, error shape hoặc quy tắc thời gian/tiền tệ.

Với thay đổi liên dự án, AI phải:

1. Xác định backend và tất cả client đang dùng contract đó bằng tìm kiếm code.
2. Giữ backward compatibility nếu có thể; không âm thầm đổi tên/xóa field.
3. Cập nhật DTO/controller, mapper/service client, mock và tài liệu contract
   trong cùng task, hoặc ghi rõ phần nào đang chờ team khác.
4. Thêm test/self-check cho mapping hoặc contract dễ bị lệch.
5. Trong bàn giao, liệt kê contract cũ, contract mới và project bị ảnh hưởng.

## 6. Kiểm tra và Definition of Done

Chỉ chạy lệnh trong project bị thay đổi, nhưng phải chạy kiểm tra phù hợp nhất:

- Backend: `dotnet build VietnamCrossBorderLogictisSystem.slnx`; chạy
  `dotnet test` khi solution có test hoặc task thêm test.
- Web nội bộ: `bun run lint`, self-check liên quan bằng `bun <file>`, sau đó
  `bun run build` cho thay đổi ảnh hưởng runtime/route/config.
- Web khách hàng: `npm run lint`; chạy `npm run build` cho thay đổi ảnh hưởng
  runtime/route/config.

Task chỉ hoàn tất khi:

- diff không chứa file ngoài phạm vi, secret hoặc generated output;
- lint/build/test liên quan pass, hoặc lỗi có sẵn được ghi rõ với bằng chứng;
- trạng thái loading/empty/error/success được xử lý cho UI có dữ liệu;
- thay đổi nghiệp vụ có test hoặc self-check hồi quy phù hợp;
- feature UI-first có contract example, canonical model, mapper và mock cùng
  shape; không có fixture domain hard-code rải rác trong component;
- phần bàn giao nêu: file đã đổi, hành vi đã đổi, lệnh đã chạy và rủi ro/việc
  còn lại. Không tuyên bố đã kiểm tra nếu chưa thực sự chạy.

## 7. Quy ước branch, commit và PR

Khi được yêu cầu tạo branch/commit, dùng tên có mục đích:
`feat/<ticket>-<mo-ta>`, `fix/<ticket>-<mo-ta>`, `chore/<mo-ta>`.
Commit dùng dạng `type(scope): summary`, ví dụ
`fix(consignments): normalize quotation status`. PR phải nhỏ, mô tả cách kiểm
tra và đánh dấu rõ breaking change, migration hoặc thay đổi contract.
