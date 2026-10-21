using Application.DTOs.Auth;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings;

/// <summary>
/// AutoMapper profiles for application layer.
/// </summary>
public class MappingProfile : Profile
{
    /// <summary>
    /// Initializes mapping configuration.
    /// </summary>
    public MappingProfile()
    {
        CreateMap<ApplicationUser, CurrentUserDto>()
            .ForMember(dest => dest.Username, opt => opt.MapFrom(src => src.UserName ?? string.Empty))
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.Email ?? string.Empty))
            .ForMember(dest => dest.Roles, opt => opt.Ignore())
            .ForMember(dest => dest.Permissions, opt => opt.Ignore());
    }
}
