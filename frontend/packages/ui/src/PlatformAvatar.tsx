import { Avatar, Tooltip } from 'antd';
import type { PlatformOption } from '@drop/types';

/** Avatar badge for platform code; tooltip shows platform display name. */
export const PlatformAvatar = ({
  platformCode,
  platforms,
  size = 18,
}: {
  platformCode?: string | null;
  platforms: PlatformOption[];
  size?: number;
}) => {
  if (!platformCode) return null;

  const hit = platforms.find((p) => p.code.toLowerCase() === platformCode.toLowerCase());
  const name = hit?.name || platformCode;

  return (
    <Tooltip title={name}>
      <Avatar
        src={hit?.avatarUrl}
        shape="square"
        size={size}
        style={{ flexShrink: 0, verticalAlign: 'middle' }}
      >
        {name.charAt(0).toUpperCase()}
      </Avatar>
    </Tooltip>
  );
};
