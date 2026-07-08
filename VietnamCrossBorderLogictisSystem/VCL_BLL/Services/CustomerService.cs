using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class CustomerService : ICustomerService
    {
        private readonly IUnitOfWork _unitOfWork;

        public CustomerService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<IEnumerable<CustomerResponseDto>> GetAllCustomersAsync(string? search)
        {
            var customerRepo = _unitOfWork.Repository<Customer>();
            var companyRepo = _unitOfWork.Repository<Company>();

            var customers = await customerRepo.GetAllAsync();
            var companies = await companyRepo.GetAllAsync();

            var query = customers.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.ToLower();
                query = query.Where(c => 
                    (c.FullName != null && c.FullName.ToLower().Contains(searchLower)) ||
                    (c.CustomerCode != null && c.CustomerCode.ToLower().Contains(searchLower)) ||
                    (c.Email != null && c.Email.ToLower().Contains(searchLower)) ||
                    (c.Phone != null && c.Phone.ToLower().Contains(searchLower)) ||
                    (c.Address != null && c.Address.ToLower().Contains(searchLower)) ||
                    (c.CompanyId.HasValue && companies.Any(co => co.Id == c.CompanyId && co.CompanyName != null && co.CompanyName.ToLower().Contains(searchLower)))
                );
            }

            var result = new List<CustomerResponseDto>();
            foreach (var c in query)
            {
                var company = c.CompanyId.HasValue ? companies.FirstOrDefault(co => co.Id == c.CompanyId.Value) : null;
                result.Add(new CustomerResponseDto
                {
                    Id = c.Id,
                    CustomerCode = c.CustomerCode ?? string.Empty,
                    FullName = c.FullName ?? string.Empty,
                    Email = c.Email ?? string.Empty,
                    Phone = c.Phone ?? string.Empty,
                    Address = c.Address ?? string.Empty,
                    CompanyName = company?.CompanyName,
                    TaxId = company?.TaxCode,
                    Status = c.Status ?? "ACTIVE"
                });
            }

            return result;
        }

        public async Task<CustomerResponseDto> GetCustomerByIdAsync(Guid id)
        {
            var customerRepo = _unitOfWork.Repository<Customer>();
            var customer = await customerRepo.GetByIdAsync(id);

            if (customer == null)
                throw new NotFoundException($"Không tìm thấy khách hàng với ID: {id}");

            Company company = null;
            if (customer.CompanyId.HasValue)
            {
                var companyRepo = _unitOfWork.Repository<Company>();
                company = await companyRepo.GetByIdAsync(customer.CompanyId.Value);
            }

            return new CustomerResponseDto
            {
                Id = customer.Id,
                CustomerCode = customer.CustomerCode ?? string.Empty,
                FullName = customer.FullName ?? string.Empty,
                Email = customer.Email ?? string.Empty,
                Phone = customer.Phone ?? string.Empty,
                Address = customer.Address ?? string.Empty,
                CompanyName = company?.CompanyName,
                TaxId = company?.TaxCode,
                Status = customer.Status ?? "ACTIVE"
            };
        }

        public async Task<CustomerResponseDto> CreateCustomerAsync(CreateCustomerRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.FullName))
                throw new ArgumentException("Họ tên khách hàng là bắt buộc.");
            if (string.IsNullOrWhiteSpace(request.Phone))
                throw new ArgumentException("Số điện thoại là bắt buộc.");

            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();

            if (allCustomers.Any(c => string.Equals(c.Phone, request.Phone, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Số điện thoại đã tồn tại trên hệ thống.");

            if (!string.IsNullOrWhiteSpace(request.Email) && 
                allCustomers.Any(c => string.Equals(c.Email, request.Email, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Email đã tồn tại trên hệ thống.");

            // Sinh CustomerCode tự động
            var maxId = allCustomers.Count() + 1;
            var customerCode = $"CUS-{maxId:D3}";
            while (allCustomers.Any(c => string.Equals(c.CustomerCode, customerCode, StringComparison.OrdinalIgnoreCase)))
            {
                maxId++;
                customerCode = $"CUS-{maxId:D3}";
            }

            Company company = null;
            if (!string.IsNullOrWhiteSpace(request.CompanyName))
            {
                var companyRepo = _unitOfWork.Repository<Company>();
                company = new Company
                {
                    Id = Guid.NewGuid(),
                    CompanyName = request.CompanyName,
                    TaxCode = request.TaxId,
                    CreatedAt = DateTime.UtcNow
                };
                await companyRepo.AddAsync(company);
            }

            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                FullName = request.FullName,
                Phone = request.Phone,
                Email = request.Email,
                Address = request.Address,
                Status = request.Status ?? "ACTIVE",
                CustomerCode = customerCode,
                CompanyId = company?.Id,
                CreatedAt = DateTime.UtcNow,
                CustomerType = "Retail"
            };

            await customerRepo.AddAsync(customer);
            await _unitOfWork.SaveChangesAsync();

            return new CustomerResponseDto
            {
                Id = customer.Id,
                CustomerCode = customer.CustomerCode,
                FullName = customer.FullName,
                Email = customer.Email,
                Phone = customer.Phone,
                Address = customer.Address,
                CompanyName = company?.CompanyName,
                TaxId = company?.TaxCode,
                Status = customer.Status
            };
        }

        public async Task<CustomerResponseDto> UpdateCustomerAsync(Guid id, UpdateCustomerRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.FullName))
                throw new ArgumentException("Họ tên khách hàng là bắt buộc.");
            if (string.IsNullOrWhiteSpace(request.Phone))
                throw new ArgumentException("Số điện thoại là bắt buộc.");

            var customerRepo = _unitOfWork.Repository<Customer>();
            var customer = await customerRepo.GetByIdAsync(id);

            if (customer == null)
                throw new NotFoundException($"Không tìm thấy khách hàng với ID: {id}");

            var allCustomers = await customerRepo.GetAllAsync();

            if (allCustomers.Any(c => c.Id != id && string.Equals(c.Phone, request.Phone, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Số điện thoại đã tồn tại trên hệ thống.");

            if (!string.IsNullOrWhiteSpace(request.Email) && 
                allCustomers.Any(c => c.Id != id && string.Equals(c.Email, request.Email, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Email đã tồn tại trên hệ thống.");

            Company company = null;
            var companyRepo = _unitOfWork.Repository<Company>();
            if (customer.CompanyId.HasValue)
            {
                company = await companyRepo.GetByIdAsync(customer.CompanyId.Value);
                if (company != null)
                {
                    if (string.IsNullOrWhiteSpace(request.CompanyName))
                    {
                        // Remove link if CompanyName cleared
                        customer.CompanyId = null;
                    }
                    else
                    {
                        company.CompanyName = request.CompanyName;
                        company.TaxCode = request.TaxId;
                        companyRepo.Update(company);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(request.CompanyName))
            {
                company = new Company
                {
                    Id = Guid.NewGuid(),
                    CompanyName = request.CompanyName,
                    TaxCode = request.TaxId,
                    CreatedAt = DateTime.UtcNow
                };
                await companyRepo.AddAsync(company);
                customer.CompanyId = company.Id;
            }

            customer.FullName = request.FullName;
            customer.Phone = request.Phone;
            customer.Email = request.Email;
            customer.Address = request.Address;
            customer.Status = request.Status ?? customer.Status ?? "ACTIVE";

            customerRepo.Update(customer);
            await _unitOfWork.SaveChangesAsync();

            return new CustomerResponseDto
            {
                Id = customer.Id,
                CustomerCode = customer.CustomerCode ?? string.Empty,
                FullName = customer.FullName,
                Email = customer.Email,
                Phone = customer.Phone,
                Address = customer.Address,
                CompanyName = company?.CompanyName,
                TaxId = company?.TaxCode,
                Status = customer.Status
            };
        }

        // VCL-157: Customer tự quản lý hồ sơ cá nhân
        public async Task<CustomerProfileResponseDto> GetMyProfileAsync(Guid userId)
        {
            var customer = await FindCustomerByUserIdOrThrowAsync(userId);
            return MapProfile(customer);
        }

        public async Task<CustomerProfileResponseDto> UpdateMyProfileAsync(Guid userId, UpdateCustomerProfileRequestDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu hồ sơ không được để trống.");
            if (string.IsNullOrWhiteSpace(request.FullName))
                throw new ArgumentException("Họ tên không được để trống.");

            var customerRepo = _unitOfWork.Repository<Customer>();
            var customer = await FindCustomerByUserIdOrThrowAsync(userId);

            // Không cho trùng SĐT với khách khác (nếu có nhập)
            if (!string.IsNullOrWhiteSpace(request.Phone))
            {
                var allCustomers = await customerRepo.GetAllAsync();
                if (allCustomers.Any(c => c.Id != customer.Id
                    && string.Equals(c.Phone, request.Phone.Trim(), StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("Số điện thoại đã tồn tại trên hệ thống.");
            }

            // Chỉ cập nhật các trường được phép; email + customerCode giữ nguyên
            customer.FullName = request.FullName.Trim();
            if (request.Phone != null) customer.Phone = request.Phone.Trim();
            if (request.Country != null) customer.Country = request.Country.Trim();
            if (request.Address != null) customer.Address = request.Address.Trim();

            customerRepo.Update(customer);
            await _unitOfWork.SaveChangesAsync();

            return MapProfile(customer);
        }

        // VCL-159: Sales vô hiệu hóa hồ sơ khách hàng (soft-delete để giữ lịch sử đơn)
        public async Task DeactivateCustomerAsync(Guid id)
        {
            var customerRepo = _unitOfWork.Repository<Customer>();
            var customer = await customerRepo.GetByIdAsync(id);
            if (customer == null)
                throw new NotFoundException($"Không tìm thấy khách hàng với ID: {id}");

            if (string.Equals(customer.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Khách hàng đã ở trạng thái ngừng hoạt động (INACTIVE).");

            customer.Status = "INACTIVE";
            customerRepo.Update(customer);
            await _unitOfWork.SaveChangesAsync();
        }

        private async Task<Customer> FindCustomerByUserIdOrThrowAsync(Guid userId)
        {
            var customers = await _unitOfWork.Repository<Customer>().GetAllAsync();
            var customer = customers.FirstOrDefault(c => c.UserId == userId);
            if (customer == null)
                throw new NotFoundException("Không tìm thấy hồ sơ khách hàng tương ứng với tài khoản.");
            return customer;
        }

        private static CustomerProfileResponseDto MapProfile(Customer c) => new CustomerProfileResponseDto
        {
            Id = c.Id,
            CustomerCode = c.CustomerCode ?? string.Empty,
            FullName = c.FullName ?? string.Empty,
            Email = c.Email ?? string.Empty,
            Phone = c.Phone ?? string.Empty,
            Country = c.Country ?? string.Empty,
            Address = c.Address ?? string.Empty,
            CustomerType = c.CustomerType ?? string.Empty,
            Status = c.Status ?? string.Empty,
            CreatedAt = c.CreatedAt
        };
    }
}
